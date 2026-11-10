using System;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 바닥 원판(독장판·착탄 예고 표식)을 Terrain 곡면에 맞춘다. 꼭짓점마다 그 자리의 Terrain 높이를 읽어 메시를 다시 만든다.
    /// PoisonZone.Begin 과 PoisonAreaAttack.OnWindup 이 원판을 놓은 직후 부른다.
    /// 메시는 지름 1m(반지름 0.5) 원판이고 Transform 스케일 (지름, 1, 지름) 으로 넓힌다 → 셰이더의 "오브젝트 XZ 거리 ÷ 0.5 = 반경 비율" 계산이 그대로 맞는다.
    /// </summary>
    /// <remarks>Terrain 만 따른다 (바위·다리 메시 위는 덮지 않음). 독장판 피해 조건(PoisonZone.IsInsideZone)과 같은 기준이다.</remarks>
    public static class TerrainDiscMesh
    {
        private const float UnitRadius = 0.5f;   // 메시 반지름 (오브젝트 공간). 스케일 1 = 지름 1m
        private const int RingCount = 8;         // 중심에서 가장자리까지 고리 수. 많을수록 곡면을 촘촘히 따른다
        private const int SegmentCount = 32;     // 고리 한 바퀴 조각 수. 많을수록 테두리가 둥글다

        /// <summary>
        /// 원판을 지름에 맞게 넓히고, 메시를 현재 위치 주변 Terrain 곡면에 맞춰 다시 만든다.
        /// </summary>
        /// <param name="disc">MeshFilter 가 붙은 원판. 위치(중심)는 이미 Terrain 표면에 놓여 있어야 한다. 회전은 없다고 가정한다</param>
        /// <param name="diameter">원판 지름 (m)</param>
        /// <param name="surfaceLift">Terrain 표면 위로 띄우는 높이 (m). 지형과 겹쳐 깜빡이지 않게 한다</param>
        public static void FitToTerrain(Transform disc, float diameter, float surfaceLift)
        {
            disc.localScale = new Vector3(diameter, 1f, diameter);

            Mesh mesh = disc.GetComponent<MeshFilter>().mesh;
            mesh.Clear();
            mesh.vertices = LocalVertices(disc.position, diameter, surfaceLift, TerrainGround.HeightAt);
            mesh.triangles = Triangles();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// 원판 꼭짓점(오브젝트 공간)을 계산한다. 중심 1개 + 고리마다 SegmentCount 개.
        /// 아이디어: 꼭짓점 XZ 를 월드로 바꿔(중심 + XZ × 지름) 그 자리 지면 높이를 읽고, 중심 높이와의 차이 + 띄움 높이를 오브젝트 Y 로 쓴다 (Y 스케일 1).
        /// </summary>
        /// <param name="center">원판 중심 월드 위치</param>
        /// <param name="diameter">원판 지름 (m)</param>
        /// <param name="surfaceLift">지면 위로 띄우는 높이 (m)</param>
        /// <param name="heightAt">월드 위치 → 지면 월드 높이 (m). 게임은 TerrainGround.HeightAt, 테스트는 직접 만든 함수</param>
        /// <returns>꼭짓점 배열. [0] = 중심, 그다음 고리 1(안쪽)부터 바깥 고리까지 각 고리를 0도부터 반시계로</returns>
        public static Vector3[] LocalVertices(Vector3 center, float diameter, float surfaceLift, Func<Vector3, float> heightAt)
        {
            var vertices = new Vector3[1 + RingCount * SegmentCount];
            vertices[0] = LocalVertex(Vector2.zero, center, diameter, surfaceLift, heightAt);

            for (int ring = 1; ring <= RingCount; ring++)
            {
                float ringRadius = UnitRadius * ring / RingCount;
                for (int segment = 0; segment < SegmentCount; segment++)
                {
                    float angle = 2f * Mathf.PI * segment / SegmentCount;
                    var localXZ = new Vector2(Mathf.Cos(angle) * ringRadius, Mathf.Sin(angle) * ringRadius);
                    vertices[VertexIndex(ring, segment)] = LocalVertex(localXZ, center, diameter, surfaceLift, heightAt);
                }
            }
            return vertices;
        }

        /// <summary>오브젝트 XZ 한 점의 오브젝트 공간 꼭짓점. Y = 그 자리 지면 높이 − 중심 높이 + 띄움 높이</summary>
        /// <param name="localXZ">오브젝트 공간 XZ (반지름 0.5 원 안)</param>
        /// <param name="center">원판 중심 월드 위치</param>
        /// <param name="diameter">원판 지름 (m). 오브젝트 XZ → 월드 거리 배율</param>
        /// <param name="surfaceLift">지면 위로 띄우는 높이 (m)</param>
        /// <param name="heightAt">월드 위치 → 지면 월드 높이 (m)</param>
        /// <returns>오브젝트 공간 꼭짓점</returns>
        private static Vector3 LocalVertex(Vector2 localXZ, Vector3 center, float diameter, float surfaceLift,
                                           Func<Vector3, float> heightAt)
        {
            Vector3 worldPoint = center + new Vector3(localXZ.x * diameter, 0f, localXZ.y * diameter);
            float localY = heightAt(worldPoint) - center.y + surfaceLift;
            return new Vector3(localXZ.x, localY, localXZ.y);
        }

        /// <summary>
        /// 삼각형 목록. 중심~고리 1 은 부채꼴, 고리 사이는 사각형 1칸 = 삼각형 2개.
        /// 위에서 볼 때 시계 방향으로 감아 윗면이 앞면이 되게 한다 (Unity 앞면 = 시계 방향, 셰이더 Cull Back).
        /// </summary>
        /// <returns>꼭짓점 번호 3개씩 묶인 배열</returns>
        private static int[] Triangles()
        {
            var triangles = new int[SegmentCount * 3 + (RingCount - 1) * SegmentCount * 6];
            int write = 0;

            for (int segment = 0; segment < SegmentCount; segment++)
            {
                int next = (segment + 1) % SegmentCount;
                triangles[write++] = 0;
                triangles[write++] = VertexIndex(1, next);
                triangles[write++] = VertexIndex(1, segment);
            }

            for (int ring = 1; ring < RingCount; ring++)
            {
                for (int segment = 0; segment < SegmentCount; segment++)
                {
                    int next = (segment + 1) % SegmentCount;
                    int inner = VertexIndex(ring, segment);
                    int innerNext = VertexIndex(ring, next);
                    int outer = VertexIndex(ring + 1, segment);
                    int outerNext = VertexIndex(ring + 1, next);

                    triangles[write++] = inner;
                    triangles[write++] = innerNext;
                    triangles[write++] = outerNext;

                    triangles[write++] = inner;
                    triangles[write++] = outerNext;
                    triangles[write++] = outer;
                }
            }
            return triangles;
        }

        /// <summary>고리·조각 번호 → 꼭짓점 배열 번호</summary>
        /// <param name="ring">고리 번호. 1 = 가장 안쪽, RingCount = 가장자리</param>
        /// <param name="segment">조각 번호. 0 ~ SegmentCount−1</param>
        /// <returns>LocalVertices 배열 안 번호</returns>
        private static int VertexIndex(int ring, int segment) => 1 + (ring - 1) * SegmentCount + segment;
    }
}
