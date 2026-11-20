using UnityEngine;

/// <summary>
/// Đồng vàng rơi ra khi tiêu diệt quái Orc:
/// - Bắn văng ra xa (4m - 7m) theo quỹ đạo parabol có độ nảy xuống đúng mặt đất Terrain.
/// - Có cột ánh sáng vàng nhẹ (Loot Beam) giúp người chơi dễ nhìn thấy từ xa để chạy lại nhặt.
/// - Chỉ hút vào người khi Player chạy sát lại gần (bán kính 2.1m) để tạo cảm giác chạy nhặt đồ thực sự.
/// </summary>
public class GoldPickup : MonoBehaviour
{
    public int goldAmount = 20;
    public float magnetRadius = 2.1f;
    public float pickupRadius = 1.15f;
    public float magnetSpeed = 12f;

    private Transform playerTransform;
    private Vector3 velocity;
    private float settledGroundY;
    private int bounceCount;
    private bool landed;
    private bool collected;
    private float spawnTime;

    public static void SpawnGoldBurst(Vector3 origin, int totalGold, int coinCount = 3)
    {
        if (totalGold <= 0) return;
        coinCount = Mathf.Clamp(coinCount, 2, 7);
        int perCoin = Mathf.Max(1, totalGold / coinCount);
        int remainder = totalGold - perCoin * coinCount;

        // Chia góc đều theo vòng tròn kèm độ lệch ngẫu nhiên để vàng văng tỏa đều ra xa
        float baseAngle = Random.Range(0f, 360f);
        float angleStep = 360f / coinCount;

        for (int i = 0; i < coinCount; i++)
        {
            int amt = perCoin + (i == 0 ? remainder : 0);
            float angleRad = (baseAngle + i * angleStep + Random.Range(-24f, 24f)) * Mathf.Deg2Rad;
            Vector3 horizontalDir = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));

            GameObject root = new GameObject("GoldCoin_Drop");
            root.transform.position = origin + Vector3.up * 0.9f;

            // 1. Hình đồng xu vàng kim bóng
            GameObject coinMesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            coinMesh.name = "CoinVisual";
            coinMesh.transform.SetParent(root.transform, false);
            Collider col = coinMesh.GetComponent<Collider>();
            if (col != null) Destroy(col);

            coinMesh.transform.localScale = new Vector3(0.46f, 0.08f, 0.46f);
            coinMesh.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Renderer r = coinMesh.GetComponent<Renderer>();
            if (r != null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Material mat = new Material(sh);
                mat.color = new Color(1f, 0.82f, 0.16f, 1f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(1f, 0.84f, 0.18f, 1f));
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.90f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.85f);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", new Color(1f, 0.72f, 0.12f) * 1.5f);
                }
                r.material = mat;
            }

            // 2. Cột sáng vàng mảnh (Loot Beam kiểu Diablo/Elden Ring) giúp dễ thấy vàng văng xa trên cỏ
            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "LootBeam";
            beam.transform.SetParent(root.transform, false);
            Collider beamCol = beam.GetComponent<Collider>();
            if (beamCol != null) Destroy(beamCol);
            beam.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            beam.transform.localScale = new Vector3(0.11f, 1.05f, 0.11f);
            Renderer br = beam.GetComponent<Renderer>();
            if (br != null)
            {
                Shader unlit = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                Material bMat = new Material(unlit);
                bMat.color = new Color(1f, 0.85f, 0.25f, 0.34f);
                br.material = bMat;
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // 3. Ánh sáng vàng ấm
            GameObject lightObj = new GameObject("GoldGlow");
            lightObj.transform.SetParent(root.transform, false);
            lightObj.transform.localPosition = Vector3.up * 0.35f;
            Light l = lightObj.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.82f, 0.22f);
            l.range = 4.2f;
            l.intensity = 2.0f;

            GoldPickup gp = root.AddComponent<GoldPickup>();
            gp.goldAmount = amt;
            gp.InitScatter(horizontalDir);
        }
    }

    private void InitScatter(Vector3 horizontalDir)
    {
        spawnTime = Time.time;
        // Lực văng xa 4.6 - 7.4 m/s + tung cao 5.6 - 7.8 m/s để vàng rơi cách xác quái 4-7 mét
        float outSpeed = Random.Range(4.6f, 7.4f);
        float upSpeed = Random.Range(5.6f, 7.8f);
        velocity = horizontalDir.normalized * outSpeed + Vector3.up * upSpeed;
        settledGroundY = SampleGroundHeight(transform.position) + 0.35f;
    }

    private void Start()
    {
        ResolvePlayer();
        if (spawnTime == 0f) spawnTime = Time.time;
    }

    private void Update()
    {
        if (collected || GameOverManager.IsGameOver || LevelUpChoiceManager.IsPaused) return;

        Transform visual = transform.Find("CoinVisual");
        if (visual != null)
        {
            visual.Rotate(Vector3.up, 200f * Time.deltaTime, Space.World);
        }

        if (!landed)
        {
            velocity.y -= 17.5f * Time.deltaTime;
            transform.position += velocity * Time.deltaTime;

            float groundAtPos = SampleGroundHeight(transform.position) + 0.35f;
            if (transform.position.y <= groundAtPos && velocity.y < 0f)
            {
                Vector3 p = transform.position;
                p.y = groundAtPos;
                transform.position = p;

                if (bounceCount < 1)
                {
                    // Nảy thêm 1 nhịp nhỏ trước khi dừng hẳn
                    bounceCount++;
                    velocity.x *= 0.45f;
                    velocity.z *= 0.45f;
                    velocity.y = Mathf.Abs(velocity.y) * 0.38f;
                }
                else
                {
                    settledGroundY = groundAtPos;
                    velocity = Vector3.zero;
                    landed = true;
                }
            }
        }
        else
        {
            Vector3 p = transform.position;
            p.y = settledGroundY + Mathf.Sin((Time.time - spawnTime) * 3.4f) * 0.10f;
            transform.position = p;
        }

        if (playerTransform == null)
        {
            ResolvePlayer();
            if (playerTransform == null) return;
        }

        // Chỉ cho phép nhặt sau khi vàng đã bay ra xa/chạm đất (0.75s) để Player thực sự chạy tới nhặt
        if (!landed && (Time.time - spawnTime) < 0.75f) return;

        Vector3 targetPos = playerTransform.position + Vector3.up * 0.9f;
        Vector3 diff = targetPos - transform.position;
        float dist = diff.magnitude;

        if (dist <= magnetRadius)
        {
            landed = true;
            transform.position = Vector3.MoveTowards(transform.position, targetPos, magnetSpeed * Time.deltaTime);
            dist = Vector3.Distance(transform.position, targetPos);
        }

        if (dist <= pickupRadius)
        {
            Collect();
        }
    }

    private void Collect()
    {
        collected = true;

        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.AddGold(goldAmount);
        }

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackGoldCollected(goldAmount);
        }

        GameAudioManager.PlayPickup();
        Destroy(gameObject);
    }

    private float SampleGroundHeight(Vector3 pos)
    {
        if (Terrain.activeTerrain != null)
        {
            return Terrain.activeTerrain.SampleHeight(pos) + Terrain.activeTerrain.transform.position.y;
        }

        if (Physics.Raycast(new Vector3(pos.x, pos.y + 40f, pos.z), Vector3.down, out RaycastHit hit, 120f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }

        return pos.y - 0.35f;
    }

    private void ResolvePlayer()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null) playerTransform = playerObj.transform;
    }
}
