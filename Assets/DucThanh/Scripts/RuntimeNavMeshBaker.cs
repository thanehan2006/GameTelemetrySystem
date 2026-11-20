using UnityEngine;
using Unity.AI.Navigation;

[DefaultExecutionOrder(-100)]
public class RuntimeNavMeshBaker : MonoBehaviour
{
    private NavMeshSurface surface;

    void Awake()
    {
        surface = GetComponent<NavMeshSurface>();
        if (surface == null)
        {
            surface = gameObject.AddComponent<NavMeshSurface>();
        }

        // Cấu hình bake NavMesh chỉ cho sàn và vật thể tĩnh
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

        // Tự động bake ngay khi chạy scene nếu chưa có NavMeshData
        if (surface.navMeshData == null)
        {
            surface.BuildNavMesh();
            Debug.Log("[RuntimeNavMeshBaker] Đã tự động Bake NavMesh cho Ground và Scene!");
        }
    }
}
