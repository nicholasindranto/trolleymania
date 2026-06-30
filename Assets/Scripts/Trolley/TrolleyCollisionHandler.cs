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

        // KODENYA TERSPESIALISASI: Ketika menabrak dinding, langsung hentikan kecepatan secara fisik
        // dan daftarkan normal kontak untuk memblokir pergerakan ke arah dinding tersebut.
        if (otherObj.CompareTag(wallTag))
        {
            if (playerTrolleyController != null)
            {
                playerTrolleyController.ForceStopSpeed();
                for (int i = 0; i < collision.contacts.Length; i++)
                {
                    playerTrolleyController.RegisterWallContact(collision.contacts[i].normal);
                }
            }
        }

        // LOGIC DI BALIK LAYAR (Pencegahan Jitter Damage):
        // Jika belum melewati masa cooldown dari damage sebelumnya, kita langsung return/skip.
        // Ini mencegah HP/Durabilitas langsung terkuras habis ketika trolley menabrak dan bergeser (jitter/slide)
        // di dinding yang sama dalam frame berturut-turut.
        if (Time.time < lastDamageTime + damageCooldown)
        {
            return;
        }

        // LOGIC DI BALIK LAYAR (Pemeriksaan Titik Kontak Tabrakan):
        // Unity mengirimkan data tabrakan beserta titik kontak fisik (contacts). 
        // Melalui kontak pertama, kita dapat memeriksa collider mana dari model compound kita 
        // (apakah player atau keranjang belanja) yang pertama kali menerima benturan.
        if (collision.contactCount == 0) return;

        Collider thisCollider = collision.GetContact(0).thisCollider;

        // LOGIC DI BALIK LAYAR (Pencegahan Durabilitas Berkurang Saat Menyerempet/Mengepot Dinding):
        // Menggunakan relativeVelocity diproyeksikan (Dot Product) ke normal kontak tabrakan.
        // Ini mengukur seberapa cepat objek menabrak secara tegak lurus (impact force nyata).
        // Jika hanya menyerempet/slide sejajar dinding, kecepatan relatif di sumbu normal akan mendekati 0,
        // sehingga tidak akan sengaja memicu pengurangan HP/durabilitas meskipun player melaju kencang (full speed).
        Vector3 collisionNormal = collision.GetContact(0).normal;
        float impactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collisionNormal));

        // Hitung kecepatan maksimal acuan player saat ini (disesuaikan berat beban cargo)
        float currentMaxSpeed = playerTrolleyController != null ? (playerTrolleyController.MaxSpeed * playerTrolleyController.WeightFactor) : 8f;

        // LOGIC DI BALIK LAYAR (Deteksi Sub-Collider):
        // Menggunakan variabel nama dan tag yang dikonfigurasi di Inspector agar fleksibel jika ada perubahan aset.
        bool isPlayerHit = thisCollider.gameObject.name == playerColliderName || thisCollider.CompareTag(playerTag);

        if (isPlayerHit)
        {
            // 1. LOGIKA KERUSAKAN HP PLAYER
            
            // KONDISI A: Player ditabrak oleh NPC (Trolley NPC maupun Karakter NPC itu sendiri)
            if (otherObj.CompareTag(npcTrolleyTag) || otherObj.CompareTag(npcTag))
            {
                float speedRatio = impactSpeed / GetNpcMaxSpeed(otherObj);

                if (speedRatio >= damageSpeedRatioThreshold)
                {
                    Debug.LogWarning("[TrolleyCollisionHandler] Player ditabrak oleh NPC/Trolley NPC pada kecepatan tinggi!");
                    if (HealthManager.Instance != null)
                    {
                        HealthManager.Instance.ReducePlayerHp(playerHpDamageFromNpc);
                        lastDamageTime = Time.time; // Aktifkan cooldown
                    }
                    if (areaDetector != null)
                    {
                        areaDetector.ShakeObjects(shakeForceMagnitude);
                    }
                }
            }
            // KONDISI B: Player menabrak dinding saat mundur
            else if (otherObj.CompareTag(wallTag))
            {
                float speedRatio = impactSpeed / currentMaxSpeed;

                if (speedRatio >= damageSpeedRatioThreshold)
                {
                    Debug.LogWarning("[TrolleyCollisionHandler] Tubuh Player menabrak dinding saat mundur pada kecepatan tinggi!");
                    if (HealthManager.Instance != null)
                    {
                        HealthManager.Instance.ReducePlayerHp(playerHpDamageFromWall);
                        lastDamageTime = Time.time; // Aktifkan cooldown
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
            
            // KONDISI A: Trolley menabrak dinding (Tag "Wall")
            if (otherObj.CompareTag(wallTag))
            {
                float speedRatio = impactSpeed / currentMaxSpeed;

                if (speedRatio >= damageSpeedRatioThreshold)
                {
                    Debug.LogWarning("[TrolleyCollisionHandler] Trolley menabrak dinding pada kecepatan tinggi!");
                    if (HealthManager.Instance != null)
                    {
                        HealthManager.Instance.ReduceTrolleyDurability(wallCollisionDamage);
                        lastDamageTime = Time.time; // Aktifkan cooldown
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
                // Gunakan kecepatan maksimal tertinggi sebagai acuan rasio benturan
                float maxAcuanSpeed = Mathf.Max(GetNpcMaxSpeed(otherObj), currentMaxSpeed);
                float speedRatio = impactSpeed / maxAcuanSpeed;

                if (speedRatio >= damageSpeedRatioThreshold)
                {
                    Debug.LogWarning("[TrolleyCollisionHandler] Tabrakan antara Trolley Player dan NPC pada kecepatan tinggi!");
                    if (HealthManager.Instance != null)
                    {
                        HealthManager.Instance.ReduceTrolleyDurability(npcTrolleyCollisionDamage);
                        lastDamageTime = Time.time; // Aktifkan cooldown
                    }
                    if (areaDetector != null)
                    {
                        areaDetector.ShakeObjects(shakeForceMagnitude);
                    }
                }
            }
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        // KODENYA TERSPESIALISASI: Selama menyentuh dinding, daftarkan normal kontak secara kontinu
        // agar TrolleyController tahu dinding mana yang menghalangi pergerakan ke depan.
        if (collision.gameObject.CompareTag(wallTag))
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

    /// <summary>
    /// Mendapatkan kecepatan maksimum dari NPCController secara dinamis berdasarkan objek tabrakan.
    /// </summary>
    private float GetNpcMaxSpeed(GameObject npcObj)
    {
        NPCController npcCtrl = npcObj.GetComponent<NPCController>();
        if (npcCtrl == null) npcCtrl = npcObj.GetComponentInParent<NPCController>();
        if (npcCtrl == null) npcCtrl = npcObj.GetComponentInChildren<NPCController>();

        if (npcCtrl != null)
        {
            return npcCtrl.MaxSpeed;
        }

        return 8f; // Fallback default
    }

    /// <summary>
    /// Mencari tahu kecepatan saat ini dari Trolley NPC secara aman menggunakan kecepatan linier Rigidbody.
    /// </summary>
    private float GetNpcTrolleySpeed(GameObject npcObj)
    {
        // LOGIC DI BALIK LAYAR (Scalable & Decoupled Speed Check):
        // Mengambil Rigidbody dari objek penabrak (atau parent/children-nya jika ada nested structure)
        // untuk mengukur besar magnitude velocity di world space.
        // Cara ini sangat aman karena tidak bergantung pada script navigasi NPC tertentu yang belum dibuat.
        Rigidbody npcRb = npcObj.GetComponent<Rigidbody>();
        if (npcRb == null) npcRb = npcObj.GetComponentInParent<Rigidbody>();
        if (npcRb == null) npcRb = npcObj.GetComponentInChildren<Rigidbody>();

        if (npcRb != null)
        {
            return npcRb.velocity.magnitude;
        }

        return 0f;
    }
}
