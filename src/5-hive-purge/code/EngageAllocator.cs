using System;
using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 토큰 모드(UseSlots = false) 전용 순수 배분 계산기. Unity 객체에 의존하지 않아 단독 테스트가 가능하다.
    /// 근접 동시 허용 수(Capacity)만큼 가까운 순으로 토큰을 주고, 나머지는 대기 링으로 보낸다.
    /// </summary>
    public static class EngageAllocator
    {
        // ── 상수 ──
        private const float GoldenAngleDeg = 137.5f;     // 목표와 같은 위치일 때 개체를 고르게 흩뿌리는 각도
        private const float SamePositionEpsilon = 1e-4f; // 같은 위치로 보는 거리 (m)

        // ── 배분 종류 (개체별 s_kind 값) ──
        private const int KindUnrestricted = 0;
        private const int KindEngage = 1;
        private const int KindWait = 2;

        // ── 호출 간 재사용 버퍼 (스레드 안전 불필요). 개수가 늘어날 때만 확장한다. 인덱스는 agents 배열 순서와 같다 ──
        private static float[] s_dist = new float[0];      // 목표까지 수평 거리
        private static int[] s_kind = new int[0];          // 이번 판정 결과 (Kind* 상수)
        private static bool[] s_revoked = new bool[0];     // 이번에 토큰을 회수당했나
        private static int[] s_sortBuffer = new int[0];    // 정렬·수집용 개체 인덱스 목록
        private static int[] s_slotOf = new int[0];        // 개체에 배정된 포위 슬롯 번호 (-1 = 없음)
        private static bool[] s_slotTaken = new bool[0];   // 슬롯 번호별 사용 중 여부

        /// <summary>
        /// 토큰 모드 배분 진입점. CrowdController.Allocate 가 배분 주기마다 부른다.
        /// agents[0..count) 를 판단해 results[0..count) 에 쓰고 집계를 돌려준다.
        /// </summary>
        /// <remarks>
        /// 규칙 순서: 입력 검증 -> 수평 거리 -> 비토큰 개체(Unrestricted, 과밀 집계)
        /// -> 기존 보유자 회수(ReleaseRadius 초과, 정원 초과 시 먼 순) -> 신규 배분(대기시간 자격, 거리 오름차순)
        /// -> 포위 슬롯(UseSlots) -> 결과 기록(Engage/Wait, WaitPoint, OrbitSign) -> 집계.
        /// 호출 간 정적 버퍼를 재사용하므로 여러 스레드에서 동시에 부르면 안 된다.
        /// </remarks>
        /// <param name="p">목표 위치·정원·반경·현재 시각 등 전역 파라미터</param>
        /// <param name="agents">개체별 현재 상태. 앞쪽 count 개만 읽는다</param>
        /// <param name="count">판단할 개체 수 (0 ~ agents.Length)</param>
        /// <param name="results">개체별 결과를 쓸 배열. 인덱스는 agents 와 같다. 길이 ≥ count</param>
        /// <returns>Engaged/Waiting/Unrestricted 수와 남은 자리 등 이번 배분의 집계</returns>
        /// <exception cref="ArgumentNullException">agents 또는 results 가 null 일 때 (Validate 가 던짐)</exception>
        /// <exception cref="ArgumentException">count 범위 밖, results.Length &lt; count, 반경 순서 위반일 때 (Validate 가 던짐)</exception>
        /// <exception cref="ArgumentOutOfRangeException">Capacity 가 음수일 때 (Validate 가 던짐)</exception>
        public static AllocationSummary Allocate(in AllocationParams p, AllocationAgent[] agents, int count, AllocationResult[] results)
        {
            Validate(p, agents, count, results);
            EnsureCapacity(count, p.UseSlots ? p.Capacity : 0);

            int uncontrolledInside = ResolveHolders(p, agents, count, out int maxHolders, out int keptHolders);
            GrantTokens(p, agents, count, maxHolders, keptHolders);
            AssignSlots(p, agents, count);
            return WriteResults(p, agents, count, results, uncontrolledInside);
        }

        /// <summary>Allocate 의 첫 단계. 입력이 잘못되면(null, 개수 범위, 음수 정원, 반경 순서 위반) 예외로 드러낸다.</summary>
        /// <param name="p">Capacity 와 반경 순서(0 &lt;= Near &lt; Waiting &lt;= Release)를 검사한다</param>
        /// <param name="agents">null 이 아니고 길이 ≥ count 여야 한다</param>
        /// <param name="count">0 ~ agents.Length</param>
        /// <param name="results">null 이 아니고 길이 ≥ count 여야 한다</param>
        /// <exception cref="ArgumentNullException">agents 또는 results 가 null 일 때</exception>
        /// <exception cref="ArgumentException">count 가 범위 밖, results.Length &lt; count, p.Slots 가 null 이거나 반경 순서 위반일 때</exception>
        /// <exception cref="ArgumentOutOfRangeException">p.Capacity 가 음수일 때</exception>
        private static void Validate(in AllocationParams p, AllocationAgent[] agents, int count, AllocationResult[] results)
        {
            if (agents == null) throw new ArgumentNullException(nameof(agents));
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (count < 0 || count > agents.Length)
                throw new ArgumentException($"count({count}) 가 agents.Length({agents.Length}) 범위를 벗어남", nameof(count));
            if (results.Length < count)
                throw new ArgumentException($"results.Length({results.Length}) < count({count})", nameof(results));
            if (p.Capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(p), $"Capacity({p.Capacity}) < 0");
            bool hasSlotSettings = p.Slots != null;
            bool isNearRadiusNonNegative = 0f <= p.NearRadius;
            bool isNearInsideWaiting = hasSlotSettings && p.NearRadius < p.Slots.WaitingRadius;
            bool isWaitingInsideRelease = hasSlotSettings && p.Slots.WaitingRadius <= p.ReleaseRadius;
            bool isRadiusOrderValid = isNearRadiusNonNegative && isNearInsideWaiting && isWaitingInsideRelease;
            if (isRadiusOrderValid == false)
                throw new ArgumentException(
                    $"반경 순서 위반: 0 <= Near({p.NearRadius}) < Waiting({p.Slots?.WaitingRadius}) <= Release({p.ReleaseRadius})", nameof(p));
        }

        /// <summary>정적 버퍼가 count / slotCount 보다 작으면 키운다. 키울 때 이전 내용은 버린다.</summary>
        /// <param name="count">이번에 판단할 개체 수. 개체별 버퍼의 최소 길이</param>
        /// <param name="slotCount">포위 슬롯 수 (UseSlots 가 아니면 0). s_slotTaken 의 최소 길이</param>
        private static void EnsureCapacity(int count, int slotCount)
        {
            if (s_dist.Length < count)
            {
                s_dist = new float[count];
                s_kind = new int[count];
                s_revoked = new bool[count];
                s_sortBuffer = new int[count];
                s_slotOf = new int[count];
            }
            if (s_slotTaken.Length < slotCount) s_slotTaken = new bool[slotCount];
        }

        /// <summary>
        /// 거리 계산, 비토큰 집계, 기존 보유자 회수(범위 이탈 + 정원 초과).
        /// 남은 보유자는 s_kind = KindEngage, 회수된 개체는 s_revoked = true 로 표시해 다음 단계에 넘긴다.
        /// </summary>
        /// <param name="p">목표 위치, NearRadius, ReleaseRadius, Capacity 를 쓴다</param>
        /// <param name="agents">개체별 현재 상태 (Position, UsesToken, HasToken)</param>
        /// <param name="count">판단할 개체 수</param>
        /// <param name="maxHolders">토큰 보유 가능 수 = Capacity - UncontrolledInside (0 이상)</param>
        /// <param name="keptHolders">회수 후 남은 기존 보유자 수</param>
        /// <returns>UncontrolledInside: 토큰 대상이 아닌데 NearRadius 안에 있는 개체 수</returns>
        private static int ResolveHolders(in AllocationParams p, AllocationAgent[] agents, int count, out int maxHolders, out int keptHolders)
        {
            int uncontrolledInside = 0;
            for (int i = 0; i < count; i++)
            {
                float dx = agents[i].Position.x - p.TargetPosition.x;
                float dz = agents[i].Position.z - p.TargetPosition.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                s_dist[i] = dist;
                s_revoked[i] = false;
                s_slotOf[i] = -1;
                s_kind[i] = agents[i].UsesToken ? KindWait : KindUnrestricted;
                if (agents[i].UsesToken == false && dist <= p.NearRadius) uncontrolledInside++;
            }

            maxHolders = Mathf.Max(0, p.Capacity - uncontrolledInside);

            // 범위 이탈 회수 + 남은 보유자 수집
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                bool isTokenHolder = agents[i].UsesToken && agents[i].HasToken;
                if (isTokenHolder == false) continue;
                if (s_dist[i] > p.ReleaseRadius) s_revoked[i] = true;
                else s_sortBuffer[n++] = i;
            }

            // 정원 초과: (거리, index) 오름차순 정렬 후 뒤(먼 쪽, 동률이면 index 큰 쪽)부터 회수
            SortByDistance(n);
            keptHolders = n;
            while (keptHolders > maxHolders)
            {
                keptHolders--;
                s_revoked[s_sortBuffer[keptHolders]] = true;
            }
            for (int k = 0; k < keptHolders; k++) s_kind[s_sortBuffer[k]] = KindEngage;

            return uncontrolledInside;
        }

        /// <summary>
        /// 자격 있는 미보유자에게 빈 자리만큼 가까운 순으로 토큰을 배분한다 (s_kind = KindEngage).
        /// 자격 = 대기 중이 아니거나(WaitingSince 음수) MinWaitSeconds 이상 기다렸음.
        /// </summary>
        /// <param name="p">현재 시각(Now)을 쓴다</param>
        /// <param name="agents">개체별 현재 상태 (UsesToken, HasToken, WaitingSince, MinWaitSeconds)</param>
        /// <param name="count">판단할 개체 수</param>
        /// <param name="maxHolders">토큰 보유 가능 수 (ResolveHolders 결과)</param>
        /// <param name="keptHolders">남은 기존 보유자 수 (ResolveHolders 결과)</param>
        private static void GrantTokens(in AllocationParams p, AllocationAgent[] agents, int count, int maxHolders, int keptHolders)
        {
            int free = Mathf.Max(0, maxHolders - keptHolders);
            if (free == 0) return;

            int n = 0;
            for (int i = 0; i < count; i++)
            {
                bool isTokenSeeker = agents[i].UsesToken && agents[i].HasToken == false;
                if (isTokenSeeker == false) continue; // 회수된 개체는 HasToken 이므로 자연히 제외
                float since = agents[i].WaitingSince;
                if (since < 0f || p.Now - since >= agents[i].MinWaitSeconds) s_sortBuffer[n++] = i;
            }

            SortByDistance(n);
            int grant = Mathf.Min(free, n);
            for (int k = 0; k < grant; k++) s_kind[s_sortBuffer[k]] = KindEngage;
        }

        /// <summary>
        /// 토큰 보유자(KindEngage)에게 360/Capacity 간격 포위 슬롯을 배정해 s_slotOf 에 쓴다 (UseSlots 일 때만).
        /// 기존 슬롯은 유지하고, 나머지는 가까운 순으로 자기 방위에 가장 가까운 빈 슬롯을 고른다.
        /// </summary>
        /// <param name="p">UseSlots, Capacity(슬롯 수), 목표 위치를 쓴다</param>
        /// <param name="agents">개체별 현재 상태 (SlotIndex, Position)</param>
        /// <param name="count">판단할 개체 수</param>
        private static void AssignSlots(in AllocationParams p, AllocationAgent[] agents, int count)
        {
            if (p.UseSlots == false) return;
            if (p.Capacity == 0) return;

            for (int s = 0; s < p.Capacity; s++) s_slotTaken[s] = false;

            // 1) 기존 슬롯 유지 (index 오름차순이므로 겹치면 index 작은 쪽이 유지)
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                if (s_kind[i] != KindEngage) continue;
                int old = agents[i].SlotIndex;
                bool isOldSlotInRange = old >= 0 && old < p.Capacity;
                bool canKeepOldSlot = isOldSlotInRange && s_slotTaken[old] == false;
                if (canKeepOldSlot)
                {
                    s_slotTaken[old] = true;
                    s_slotOf[i] = old;
                }
                else
                {
                    s_sortBuffer[n++] = i;
                }
            }

            // 2) 나머지는 가까운 순으로 자기 방위에 가장 가까운 빈 슬롯 선택
            SortByDistance(n);
            float step = 360f / p.Capacity;
            for (int k = 0; k < n; k++)
            {
                int i = s_sortBuffer[k];
                float bearing = Bearing(agents[i].Position, p.TargetPosition);
                int best = -1;
                float bestDiff = float.MaxValue;
                for (int s = 0; s < p.Capacity; s++)
                {
                    if (s_slotTaken[s]) continue;
                    float diff = AngleDiff(bearing, s * step);
                    if (diff < bestDiff) // 엄격 비교: 동률이면 작은 슬롯 번호 유지
                    {
                        bestDiff = diff;
                        best = s;
                    }
                }
                s_slotTaken[best] = true;
                s_slotOf[i] = best;
            }
        }

        /// <summary>판정(s_kind)을 개체별 AllocationResult(지시 + 다음 판단용 상태)로 기록하고 집계를 만든다. Allocate 의 마지막 단계.</summary>
        /// <param name="p">Capacity(슬롯 각도 간격), Now(대기 시작 시각), 대기 링 계산값을 쓴다</param>
        /// <param name="agents">개체별 현재 상태 (OrbitSign, WaitingSince, Position)</param>
        /// <param name="count">판단할 개체 수</param>
        /// <param name="results">개체별 결과를 쓸 배열 (인덱스 = agents 인덱스)</param>
        /// <param name="uncontrolledInside">ResolveHolders 가 센 비토큰 근접 개체 수. 집계와 남은 자리 계산에 쓴다</param>
        /// <returns>Engaged/Waiting/Unrestricted 수, UncontrolledInside, 남은 자리(FreeCapacity)</returns>
        private static AllocationSummary WriteResults(in AllocationParams p, AllocationAgent[] agents, int count,
                                                      AllocationResult[] results, int uncontrolledInside)
        {
            int engaged = 0, waiting = 0, unrestricted = 0;
            float step = p.Capacity > 0 ? 360f / p.Capacity : 0f;

            for (int i = 0; i < count; i++)
            {
                int kind = s_kind[i];

                // A) 토큰 비대상: 제한 없이 이동
                if (kind == KindUnrestricted)
                {
                    unrestricted++;
                    results[i] = new AllocationResult
                    {
                        Directive = new CrowdDirective(DirectiveKind.Unrestricted, default, 0, false, 0f),
                        HasToken = false,
                        WaitingSince = -1f,
                        OrbitSign = 0,
                        SlotIndex = -1,
                    };
                    continue;
                }

                // B) 토큰 보유(Engage): 배정된 슬롯 방위와 함께 근접 허용. 선회 방향은 기존 값 유지, 없으면 index 홀짝으로 분산
                int orbit = ResolveOrbitSign(agents[i].OrbitSign, i);

                if (kind == KindEngage)
                {
                    engaged++;
                    int slot = s_slotOf[i];
                    bool hasSlot = slot >= 0;
                    results[i] = new AllocationResult
                    {
                        Directive = new CrowdDirective(DirectiveKind.Engage, default, orbit, hasSlot, hasSlot ? slot * step : 0f),
                        HasToken = true,
                        WaitingSince = -1f,
                        OrbitSign = orbit,
                        SlotIndex = hasSlot ? slot : -1,
                    };
                    continue;
                }

                // C) 토큰 없음(Wait): 대기 링 위 지점으로 보내고 대기 시작 시각을 기록
                waiting++;
                // 회수된 개체는 이번 판단 시각부터 대기 시작
                bool keepsWaitingSince = s_revoked[i] == false && agents[i].WaitingSince >= 0f;
                float since = keepsWaitingSince ? agents[i].WaitingSince : p.Now;
                results[i] = new AllocationResult
                {
                    Directive = new CrowdDirective(DirectiveKind.Wait, WaitPoint(p, agents[i].Position, s_dist[i], i), orbit, false, 0f),
                    HasToken = false,
                    WaitingSince = since,
                    OrbitSign = orbit,
                    SlotIndex = -1,
                };
            }

            return new AllocationSummary
            {
                Engaged = engaged,
                Waiting = waiting,
                Unrestricted = unrestricted,
                UncontrolledInside = uncontrolledInside,
                FreeCapacity = Mathf.Max(0, p.Capacity - uncontrolledInside - engaged),
            };
        }

        /// <summary>WriteResults 가 Engage/Wait 개체의 선회 방향을 정할 때 쓴다. 기존 방향이 있으면 유지하고, 없으면 index 홀짝으로 나눠 군중이 양쪽으로 분산되게 한다.</summary>
        /// <param name="currentSign">개체의 기존 선회 방향 (+1/-1, 0 = 미지정)</param>
        /// <param name="index">개체 인덱스. 미지정일 때 짝수 = +1, 홀수 = -1</param>
        /// <returns>선회 방향 (+1 또는 -1)</returns>
        private static int ResolveOrbitSign(int currentSign, int index)
        {
            if (currentSign != 0) return currentSign;
            return (index & 1) == 0 ? 1 : -1;
        }

        /// <summary>
        /// 토큰 없는 개체를 보낼 대기 링(Slots.WaitingRadius) 위 지점을 구한다.
        /// 목표에서 개체 쪽 방향으로 링 위 점을 잡고, 목표와 수평 위치가 같으면 index 기반 황금각 방위를 쓴다.
        /// </summary>
        /// <param name="p">목표 위치와 Slots.WaitingRadius 를 쓴다</param>
        /// <param name="pos">개체 위치</param>
        /// <param name="dist">개체와 목표의 수평 거리 (m)</param>
        /// <param name="index">개체 인덱스. 같은 위치일 때 방위를 흩뿌리는 데 쓴다</param>
        /// <returns>대기 링 위 지점 (y = 목표 높이)</returns>
        private static Vector3 WaitPoint(in AllocationParams p, Vector3 pos, float dist, int index)
        {
            float dirX, dirZ;
            if (dist < SamePositionEpsilon)
            {
                float rad = index * GoldenAngleDeg * Mathf.Deg2Rad;
                dirX = Mathf.Sin(rad);
                dirZ = Mathf.Cos(rad);
            }
            else
            {
                dirX = (pos.x - p.TargetPosition.x) / dist;
                dirZ = (pos.z - p.TargetPosition.z) / dist;
            }
            return new Vector3(p.TargetPosition.x + dirX * p.Slots.WaitingRadius, p.TargetPosition.y, p.TargetPosition.z + dirZ * p.Slots.WaitingRadius);
        }

        /// <summary>AssignSlots 가 개체에 가장 가까운 슬롯을 고를 때 쓰는 목표 기준 방위각.</summary>
        /// <param name="pos">개체 위치</param>
        /// <param name="target">목표 위치</param>
        /// <returns>방위각 [0, 360) 도, atan2(dx, dz). +Z = 0도</returns>
        private static float Bearing(Vector3 pos, Vector3 target)
        {
            float deg = Mathf.Atan2(pos.x - target.x, pos.z - target.z) * Mathf.Rad2Deg;
            if (deg < 0f) deg += 360f;
            if (deg >= 360f) deg -= 360f;
            return deg;
        }

        /// <summary>두 방위각의 원형 최소 차이. AssignSlots 의 슬롯 선택 기준.</summary>
        /// <param name="a">각도 (도)</param>
        /// <param name="b">각도 (도)</param>
        /// <returns>원형 최소 각도 차 (0~180 도)</returns>
        private static float AngleDiff(float a, float b)
        {
            float d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        /// <summary>s_sortBuffer[0..n) 의 개체 인덱스를 (s_dist 거리, index) 오름차순으로 삽입 정렬한다 (할당 없음).</summary>
        /// <param name="n">정렬할 앞쪽 원소 수</param>
        private static void SortByDistance(int n)
        {
            for (int i = 1; i < n; i++)
            {
                int key = s_sortBuffer[i];
                float kd = s_dist[key];
                int j = i - 1;
                while (j >= 0 && Less(kd, key, s_dist[s_sortBuffer[j]], s_sortBuffer[j]))
                {
                    s_sortBuffer[j + 1] = s_sortBuffer[j];
                    j--;
                }
                s_sortBuffer[j + 1] = key;
            }
        }

        /// <summary>SortByDistance 의 (거리, index) 순서 비교. 거리가 같으면 index 작은 쪽이 앞 (결정적 정렬)</summary>
        /// <param name="distA">A 의 거리 (m)</param>
        /// <param name="indexA">A 의 개체 인덱스</param>
        /// <param name="distB">B 의 거리 (m)</param>
        /// <param name="indexB">B 의 개체 인덱스</param>
        /// <returns>A 가 B 보다 앞이면 true</returns>
        private static bool Less(float distA, int indexA, float distB, int indexB)
        {
            bool isCloser = distA < distB;
            bool isSameDistance = distA == distB;
            bool hasSmallerIndex = indexA < indexB;
            return isCloser || (isSameDistance && hasSmallerIndex);
        }
    }
}
