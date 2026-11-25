#!/usr/bin/env node
import { run } from './check-deck.mjs';

await run(['4-wgo', ...process.argv.slice(2)], { flatReport: true });
