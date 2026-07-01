using UnityEngine;

/// <summary>
/// Script ini ditempelkan pada GameObject induk Trolley (yang memiliki Rigidbody dan TrolleyController).
/// Berfungsi sebagai pusat penangan tabrakan fisik (OnCollisionEnter) untuk mengurangi HP Player 
/// atau Durabilitas Trolley berdasarkan kondisi kecepatan tabrakan dan tipe objek yang ditabrak.
/// </summary>
[RequireComponent(typeof(TrolleyController))]
public class TrolleyCollisionHandler : MonoBehaviour
{
    // Kecepatan maksimum NPC sekarang dideteksi dinamis dari NPCController.MaxSpeed untuk mendukung preset yang berbeda-beda.

    [Header("Damage Settings")]
    [Tooltip("Jumlah pengurangan durabilitas trolley saat menabrak dinding pada kecepatan tinggi.")]
    [SerializeField] private int wallCollisionDamage = 1;

    [Tooltip("Jumlah pengurangan durabilitas trolley saat ditabrak Trolley NPC pada kecepatan tinggi.")]
    [SerializeField] private int npcTrolleyCollisionDamage = 1;

    [Tooltip("Jumlah pengurangan HP player saat badan player ditabrak Trolley NPC pada kecepatan tinggi.")]
    [SerializeField] private int playerHpDamageFromNpc = 1;

    [Header("Cooldown Settings")]
    [Tooltip("Waktu tunggu (cooldown) minimal setelah terkena damage sebelum bisa terkena damage lagi (mencegah jitter).")]
    [SerializeField] private float damageCooldown = 1.0f;

    [Header("Collision Configs")]
    [Tooltip("Rasio kecepatan minimum (0-1) dari kecepatan maksimum acuan untuk memicu kerusakan (default 75% yaitu 0.75).")]
    [SerializeField] private float damageSpeedRatioThreshold = 0.75f;

    [Tooltip("Nama GameObject collider Player untuk deteksi tabrakan tubuh player.")]
    [SerializeField] private string playerColliderName = "Player";

    [Tooltip("Tag dari objek Player.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Tag objek dinding yang bisa dirusak jika ditabrak.")]
    [SerializeField] private string wallTag = "Wall";

    [Tooltip("Tag dari objek Trolley NPC.")]
    [SerializeField] private string npcTrolleyTag = "NPCTrolley";

    [Tooltip("Tag dari objek NPC (karakter).")]
    [SerializeField] private string npcTag = "NPC";

    [Tooltip("Jumlah pengurangan HP player saat badan player menabrak dinding ketika mundur.")]
    [SerializeField] private int playerHpDamageFromWall = 1;

    [Header("Trolley References (WebGL Mobile Optimization)")]
    [Tooltip("Referensi ke TrolleyAreaDetector untuk mendeteksi barang di dalam trolley.")]
    [SerializeField] private TrolleyAreaDetector areaDetector;

    [Tooltip("Kekuatan dorong acak (impulse) yang diberikan ke barang saat terjadi tabrakan keras.")]
    [SerializeField] private float shakeForceMagnitude = 2.0f;

    private TrolleyController playerTrolleyController;
    private float lastDamageTime = -10f; // Diset negatif agar bisa langsung menerima damage di awal

    private void Start()
    {
        // Ambil referensi controller trolley milik player untuk mengecek kecepatan bergeraknya
        playerTrolleyController = GetComponent<TrolleyController>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        GameObject otherObj = collision.gameObject;

        // KODENYA TERSPESIALISASI: Ketika menabrak dinding atau rintangan statis, langsung hentikan kecepatan secara fisik
        // dan daftarkan normal kontak untuk memblokir pergerakan ke arah rintangan tersebut.
        bool isWallOrStatic = otherObj.CompareTag(wallTag) || 
                              (!otherObj.CompareTag("Goods") && 
                               !otherObj.CompareTag("NPC") && 
                               !otherObj.CompareTag("NPCTrolley") && 
                               !otherObj.CompareTag("Player") && 
                               (otherObj.GetComponent<Rigidbody>() == null || otherObj.GetComponent<Rigidbody>().isKinematic));

        if (isWallOrStatic)
        {
            if (playerTrolleyController != null)
            {
                playerTrolleyController.ForceStopSpeed();
                int contactCount = collision.contactCount;
                for (int i = 0; i < contactCount; i++)
                {
                    playerTrolleyController.RegisterWallContact(collision.GetContact(i).normal);
                }
            }
        }

        // LOGIC DI BALIK LAYAR (Pencegahan Jitter Damage):
        if (Time.time < lastDamageTime + damageCooldown)
        {
            return;
        }

        if (collision.contactCount == 0) return;

        Collider thisCollider = collision.GetContact(0).thisCollider;
        Vector3 collisionNormal = collision.GetContact(0).normal;
        float impactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collisionNormal));

        // Hitung kecepatan maksimal acuan player saat ini (disesuaikan berat beban cargo)
        float currentMaxSpeed = playerTrolleyController != null ? (playerTrolleyController.MaxSpeed * playerTrolleyController.WeightFactor) : 12f;

        // Deteksi sub-collider (Player vs Trolley)
        bool isPlayerHit = thisCollider.gameObject.name == playerColliderName || thisCollider.CompareTag(playerTag);

        if (isPlayerHit)
        {
            // 1. LOGIKA KERUSAKAN HP PLAYER
            
            // KONDISI A: Player ditabrak oleh NPC (Trolley NPC maupun Karakter NPC itu sendiri)
            if (otherObj.CompareTag(npcTrolleyTag) || otherObj.CompareTag(npcTag))
            {
                // KODENYA TERSPESIALISASI: Langsung kurangi HP tanpa cek speed ratio
                Debug.LogWarning("[TrolleyCollisionHandler] Player bertabrakan dengan NPC/Trolley NPC!");
                if (HealthManager.Instance != null)
                {
                    HealthManager.Instance.ReducePlayerHp(playerHpDamageFromNpc);
                    lastDamageTime = Time.time;
                }
                if (areaDetector != null)
                {
                    areaDetector.ShakeObjects(shakeForceMagnitude);
                }
                ApplyNpcKnockback(otherObj);
            }
            // KONDISI B: Player menabrak dinding saat mundur
            else if (isWallOrStatic)
            {
                float speedRatio = impactSpeed / currentMaxSpeed;

                // Hitung sudut benturan terhadap dinding (90 = tegak lurus, 0 = sejajar/scraping)
                float angleToNormal = Vector3.Angle(transform.forward, collisionNormal);
                float angleWithWall = Mathf.Abs(90f - angleToNormal);
                bool isAnglePunishing = angleWithWall >= 45f && angleWithWall <= 135f;

                if (speedRatio >= damageSpeedRatioThreshold && isAnglePunishing)
                {
                    Debug.LogWarning("[TrolleyCollisionHandler] Tubuh Player menabrak dinding saat mundur pada kecepatan tinggi dengan sudut menukik!");
                    if (HealthManager.Instance != null)
                    {
                        HealthManager.Instance.ReducePlayerHp(playerHpDamageFromWall);
                        lastDamageTime = Time.time;
                    }
                    if (areaDetector != null)
                    {
                        areaDetector.ShakeObjects(shakeForceMagnitude);
                    }
                }
            }
        }
        else
        {
            // 2. LOGIKA KERUSAKAN TROLLEY DURABILITY
            
            // KONDISI A: Trolley menabrak dinding / rintangan statis
            if (isWallOrStatic)
            {
                float speedRatio = impactSpeed / currentMaxSpeed;

                // Hitung sudut benturan terhadap dinding (90 = tegak lurus, 0 = sejajar/scraping)
                float angleToNormal = Vector3.Angle(transform.forward, collisionNormal);
                float angleWithWall = Mathf.Abs(90f - angleToNormal);
                bool isAnglePunishing = angleWithWall >= 45f && angleWithWall <= 135f;

                if (speedRatio >= damageSpeedRatioThreshold && isAnglePunishing)
                {
                    Debug.LogWarning("[TrolleyCollisionHandler] Trolley menabrak dinding pada kecepatan tinggi dengan sudut menukik!");
                    if (HealthManager.Instance != null)
                    {
                        HealthManager.Instance.ReduceTrolleyDurability(wallCollisionDamage);
                        lastDamageTime = Time.time;
                    }
                    if (areaDetector != null)
                    {
                        areaDetector.ShakeObjects(shakeForceMagnitude);
                    }
                }
            }
            // KONDISI B: Trolley Player tabrakan dengan NPC (Trolley NPC atau NPC itu sendiri)
            else if (otherObj.CompareTag(npcTrolleyTag) || otherObj.CompareTag(npcTag))
            {
                // KODENYA TERSPESIALISASI: Langsung kurangi durabilitas trolley tanpa cek speed ratio
                Debug.LogWarning("[TrolleyCollisionHandler] Trolley Player bertabrakan dengan NPC/Trolley NPC!");
                if (HealthManager.Instance != null)
                {
                    HealthManager.Instance.ReduceTrolleyDurability(npcTrolleyCollisionDamage);
                    lastDamageTime = Time.time;
                }
                if (areaDetector != null)
                {
                    areaDetector.ShakeObjects(shakeForceMagnitude);
                }
                ApplyNpcKnockback(otherObj);
            }
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        GameObject otherObj = collision.gameObject;
        bool isWallOrStatic = otherObj.CompareTag(wallTag) || 
                              (!otherObj.CompareTag("Goods") && 
                               !otherObj.CompareTag("NPC") && 
                               !otherObj.CompareTag("NPCTrolley") && 
                               !otherObj.CompareTag("Player") && 
                               (otherObj.GetComponent<Rigidbody>() == null || otherObj.GetComponent<Rigidbody>().isKinematic));

        if (isWallOrStatic)
        {
            if (playerTrolleyController != null)
            {
                int contactCount = collision.contactCount;
                for (int i = 0; i < contactCount; i++)
                {
                    playerTrolleyController.RegisterWallContact(collision.GetContact(i).normal);
                }
            }
        }
    }

    private void ApplyNpcKnockback(GameObject npcObj)
    {
        if (playerTrolleyController == null) return;

        NPCController npc = npcObj.GetComponent<NPCController>();
        if (npc == null) npc = npcObj.GetComponentInParent<NPCController>();
        if (npc == null) npc = npcObj.GetComponentInChildren<NPCController>();

        if (npc != null)
        {
            float npcMaxSpeed = npc.MaxSpeed;
            float npcCurrentSpeed = npc.CurrentSpeed;
            float ratio = npcMaxSpeed > 0.001f ? (npcCurrentSpeed / npcMaxSpeed) : 0f;

            float knockbackForce = 0f;
            if (ratio > 0.75f)
            {
                knockbackForce = playerTrolleyController.KnockbackForceHigh;
            }
            else if (ratio > 0.50f)
            {
                knockbackForce = playerTrolleyController.KnockbackForceMedium;
            }
            else if (ratio > 0.25f)
            {
                knockbackForce = playerTrolleyController.KnockbackForceLow;
            }

            if (knockbackForce > 0f)
            {
                Vector3 knockbackDir = (transform.position - npc.transform.position);
                knockbackDir.y = 0f; // Tetap horizontal
                if (knockbackDir.sqrMagnitude < 0.001f)
                {
                    knockbackDir = -transform.forward;
                }
                playerTrolleyController.ApplyKnockback(knockbackDir.normalized, knockbackForce);
            }
        }
    }
}
