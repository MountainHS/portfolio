"""Read-only PDF page and internal navigation check against a browser print report."""

import argparse
import json
import sys
from collections import Counter
from pathlib import Path

from pypdf import PdfReader
from pypdf.errors import PyPdfError
from pypdf.generic import ArrayObject, Destination, NumberObject


def scene_name(value):
    return str(value).lstrip('/#')


def check(pdf_path, report_path, project_id):
    report = json.loads(Path(report_path).read_text(encoding='utf-8-sig'))
    projects = [p for p in report['projects'] if p['projectId'] == project_id]
    if len(projects) != 1:
        raise ValueError(f'Expected one report project: {project_id}, found {len(projects)}')
    planned = projects[0]['print']
    ids, expected = planned['ids'], planned['links']
    if not ids or any(not isinstance(name, str) or not name for name in ids) or len(set(ids)) != len(ids):
        raise ValueError('print.ids must contain unique nonempty scene names')
    pages_by_scene = {name: index + 1 for index, name in enumerate(ids)}
    errors = []

    def error(kind, **details):
        errors.append({'type': kind, **details})

    targets = dict(pages_by_scene)
    valid_expected = []
    for link in expected:
        source, page, target, target_page = link['sceneId'], link['page'], scene_name(link['target']), link['targetPage']
        target_scene = link.get('targetSceneId', target)
        if (pages_by_scene.get(source) != page or pages_by_scene.get(target_scene) != target_page
                or (target in targets and targets[target] != target_page)):
            error('invalid-report-link', **link)
        else:
            targets[target] = target_page
            valid_expected.append(link)

    reader = PdfReader(pdf_path)
    count = len(reader.pages)
    if count != len(ids):
        error('page-count', expected=len(ids), actual=count)
    for page_number, page in enumerate(reader.pages, 1):
        unit = float(page.get('/UserUnit', 1))
        size = [float(page.mediabox.width) * unit, float(page.mediabox.height) * unit]
        if any(abs(actual - wanted) > 0.5 for actual, wanted in zip(size, [960, 540])) or int(page.get('/Rotate', 0)) % 180:
            error('page-size', page=page_number, expected=[960, 540], actual=size, rotation=page.get('/Rotate', 0))

    named = reader.named_destinations
    names_by_scene = {scene_name(name): destination for name, destination in named.items()}

    def resolve(destination, seen=None):
        seen = set() if seen is None else seen
        if isinstance(destination, Destination):
            index = reader.get_destination_page_number(destination)
            return index + 1 if index is not None and index >= 0 else None, None
        if hasattr(destination, 'get_object'):
            destination = destination.get_object()
        if isinstance(destination, (str, bytes)):
            name = scene_name(destination.decode('utf-8') if isinstance(destination, bytes) else destination)
            if name in seen:
                raise ValueError(f'Circular named destination: {name}')
            if name not in names_by_scene:
                raise ValueError(f'Unknown named destination: {name}')
            page, _ = resolve(names_by_scene[name], seen | {name})
            return page, name
        if isinstance(destination, dict) and '/D' in destination:
            return resolve(destination['/D'], seen)
        if isinstance(destination, (ArrayObject, list)) and destination:
            page = destination[0]
            if isinstance(page, (NumberObject, int)):
                return int(page) + 1, None
            page = page.get_object() if hasattr(page, 'get_object') else page
            index = reader.get_page_number(page)
            return index + 1 if index is not None and index >= 0 else None, None
        raise ValueError(f'Unsupported PDF destination: {destination!r}')

    for name, destination in named.items():
        scene = scene_name(name)
        if scene in targets:
            page, _ = resolve(destination)
            if page != targets[scene]:
                error('named-scene-page', target=scene, expected=targets[scene], actual=page)

    actual_links, external, other_actions = [], 0, []
    for page_number, page in enumerate(reader.pages, 1):
        for reference in page.get('/Annots', []):
            annotation = reference.get_object()
            if annotation.get('/Subtype') != '/Link':
                continue
            action = annotation.get('/A')
            action = action.get_object() if action is not None else None
            if '/Dest' in annotation:
                destination = annotation['/Dest']
            elif action and action.get('/S') == '/GoTo':
                destination = action.get('/D')
            elif action and action.get('/S') in ('/URI', '/GoToR', '/GoToE', '/Launch'):
                external += 1
                continue
            else:
                other_actions.append({'page': page_number, 'action': str(action.get('/S')) if action else None})
                continue
            try:
                target_page, target = resolve(destination)
            except (ValueError, TypeError, KeyError, IndexError) as exc:
                error('invalid-internal-link', page=page_number, detail=str(exc))
                continue
            link = {'page': page_number, 'targetPage': target_page, 'target': target}
            actual_links.append(link)
            if target_page is None or not 1 <= target_page <= count:
                error('invalid-target-page', **link)
            if target is not None:
                if target not in targets:
                    error('unknown-target-scene', **link)
                elif target_page != targets[target]:
                    error('wrong-target-page', expected=targets[target], **link)

    named_links = Counter((link['page'], link['target']) for link in actual_links if link['target'] is not None)
    direct_links = Counter((link['page'], link['targetPage']) for link in actual_links if link['target'] is None)
    for link in valid_expected:
        named_key = (link['page'], scene_name(link['target']))
        direct_key = (link['page'], link['targetPage'])
        if named_links[named_key]:
            named_links[named_key] -= 1
        elif direct_links[direct_key]:
            direct_links[direct_key] -= 1
        else:
            error('missing-expected-link', **link)
    return {
        'ok': not errors, 'pdf': str(Path(pdf_path).resolve()), 'projectId': project_id,
        'pages': count, 'expectedPages': len(ids), 'internalLinks': len(actual_links),
        'expectedLinks': len(expected), 'externalLinks': external,
        'otherLinkActions': other_actions, 'links': actual_links, 'errors': errors,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pdf')
    parser.add_argument('browser_report')
    parser.add_argument('project_id')
    args = parser.parse_args()
    try:
        result = check(args.pdf, args.browser_report, args.project_id)
    except (OSError, ValueError, KeyError, TypeError, PyPdfError) as exc:
        result = {'ok': False, 'errors': [{'type': 'input-or-pdf-error', 'detail': str(exc)}]}
    print(json.dumps(result, ensure_ascii=True, indent=2))
    return 0 if result['ok'] else 1


if __name__ == '__main__':
    sys.exit(main())
