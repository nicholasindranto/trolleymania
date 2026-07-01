using UnityEngine;



/// <summary>
/// Status penempatan/keberadaan barang saat ini.
/// </summary>
public enum ObjectStatus
{
    Grounded, // Objek berada bebas di tanah atau rak (bisa diambil)
    Taken     // Objek sudah berada di dalam Trolley atau digenggam (tidak bisa diambil lagi)
}

/// <summary>
/// Script ini ditempelkan pada setiap prefab atau objek fisik barang di supermarket.
/// Berperan sebagai jembatan antara metadata statis (ScriptableObject) dengan keadaan fisik dinamis objek tersebut di scene.
/// </summary>
public class ObjectScript : MonoBehaviour
{
    [Header("Data Template Reference")]
    [Tooltip("Aset ScriptableObject yang menyimpan konfigurasi statis (berat, nama, jenis) barang ini.")]
    [SerializeField] private SupermarketObjectData objectData;

    [Header("Dynamic Instance State")]
    [Tooltip("Status keberadaan objek spesifik ini secara realtime. (Grounded / Taken).")]
    [SerializeField] private ObjectStatus currentStatus = ObjectStatus.Grounded;

    [Header("Physics Sleep Settings")]
    [Tooltip("Ambang batas kecepatan di bawah mana Rigidbody akan dinonaktifkan kembali.")]
    [SerializeField] private float objMoveThreshold = 0.01f;

    [Header("Physics Sleep References (Assigned in Inspector for maximum WebGL performance)")]
    [Tooltip("Reference to the Rigidbody component.")]
    [SerializeField] private Rigidbody rb;

    [Tooltip("Reference to the ObjectAreaTrigger script attached to the area child.")]
    [SerializeField] private ObjectAreaTrigger areaTrigger;

    private bool isPhysicsActive = false;
    private bool isPermanentlySleeping = false;
    private float activeTime = 0f;

    [Header("Optimasi WebGL Settings")]
    [Tooltip("Interval waktu (detik) untuk memantau status keheningan fisik objek.")]
    [SerializeField] private float sleepCheckInterval = 0.2f;

    private Coroutine sleepMonitorCoroutine;

    // Referensi ke transform parent di trolley saat tidur
    private Transform trolleyParent;

    // OPTIMALISASI MOBILE WEBGL (BUG FIX #1):
    // Cache seluruh collider fisik di Awake agar SetCollidersEnabled TIDAK mengalokasikan array baru setiap dipanggil.
    // GetComponentsInChildren di dalam loop adalah penyebab GC pressure di Mobile WebGL — ini diperbaiki di sini.
    private Collider areaTriggerCollider;
    private Collider[] cachedColliders; // Semua collider pada objek ini (termasuk child)

    // KODENYA TERSPESIALISASI: Nama layer yang digunakan untuk membedakan objek di dalam dan di luar trolley.
    // Pastikan layer "Goods" dan "GoodsInTrolley" sudah dibuat di Project Settings > Tags and Layers.
    private const string LAYER_GOODS = "Goods";
    private const string LAYER_GOODS_IN_TROLLEY = "GoodsInTrolley";

    // Cache layer index agar tidak memanggil LayerMask.NameToLayer() di runtime (string lookup berat)
    private int layerGoods;
    private int layerGoodsInTrolley;

    private void Awake()
    {
        // OPTIMALISASI MOBILE WEBGL:
        // Menghemat performa CPU dengan memastikan Rigidbody berada dalam keadaan mati/kinematic saat pertama kali di-spawn.
        // Penyetelan di Awake ini sangat aman untuk mencegah human-error lupa men-set prefab di Inspector.
        // if (rb != null)
        // {
        //     rb.isKinematic = true;
        //     rb.useGravity = false;
        // }

        // KODENYA TERSPESIALISASI (BUG FIX #1 — Zero-Alloc SetCollidersEnabled):
        // Cache SEMUA collider sekali di Awake. Ini mencegah GetComponentsInChildren mengalokasikan
        // array baru setiap kali SetCollidersEnabled dipanggil (saat setiap fisika berubah state).
        cachedColliders = GetComponentsInChildren<Collider>(true);

        // Cache khusus areaTrigger collider agar bisa di-skip di dalam loop SetCollidersEnabled
        if (areaTrigger != null)
        {
            areaTriggerCollider = areaTrigger.GetComponent<Collider>();
        }

        // Cache layer index via string lookup sekali di Awake agar tidak ada overhead di runtime
        layerGoods = LayerMask.NameToLayer(LAYER_GOODS);
        layerGoodsInTrolley = LayerMask.NameToLayer(LAYER_GOODS_IN_TROLLEY);

        // Validasi: Jika layer tidak ditemukan, tampilkan peringatan agar developer langsung tahu setup yang harus dilakukan
        if (layerGoods == -1)
            Debug.LogWarning($"[ObjectScript] Layer '{LAYER_GOODS}' tidak ditemukan! Buat di Project Settings > Tags and Layers.");
        if (layerGoodsInTrolley == -1)
            Debug.LogWarning($"[ObjectScript] Layer '{LAYER_GOODS_IN_TROLLEY}' tidak ditemukan! Buat di Project Settings > Tags and Layers.");
    }

    private void Start()
    {
        // LOGIC DI BALIK LAYAR:
        // Saat game dimulai, inisialisasi status objek menggunakan status default yang terdefinisi di ScriptableObject.
        if (objectData != null)
        {
            currentStatus = objectData.defaultStatus;
        }

        if (rb != null)
        {
            // Jika objek di-spawn dalam keadaan fisika aktif (misal di Inspector/Spawn isKinematic = false),
            // kita harus set isPhysicsActive = true agar sistem FixedUpdate memantau kapan ia diam untuk ditidurkan.
            if (!rb.isKinematic)
            {
                isPhysicsActive = true;
                activeTime = 0f;
                if (sleepMonitorCoroutine != null) StopCoroutine(sleepMonitorCoroutine);
                sleepMonitorCoroutine = StartCoroutine(PhysicsSleepMonitorCoroutine());
            }
        }
    }

    /// <summary>
    /// Menyetel parent transform trolley secara logis ketika barang masuk ke area deteksi trolley.
    /// </summary>
    public void SetTrolleyParent(Transform parent)
    {
        trolleyParent = parent;
    }

    /// <summary>
    /// Mengaktifkan atau menonaktifkan seluruh collider fisik pada barang belanjaan ini.
    /// Menghindari collider bertabrakan dengan bodi trolley yang memicu feedback physics loop.
    /// OPTIMALISASI (BUG FIX #1): Menggunakan cachedColliders yang di-cache di Awake.
    /// TIDAK ADA alokasi array baru di runtime — zero GC pressure di Mobile WebGL.
    /// </summary>
    private void SetCollidersEnabled(bool enabled)
    {
        // Gunakan array yang sudah di-cache di Awake — tidak ada alokasi baru sama sekali
        if (cachedColliders == null) return;

        for (int i = 0; i < cachedColliders.Length; i++)
        {
            // KODENYA TERSPESIALISASI: Jangan matikan collider areaTrigger (IsTrigger) karena digunakan
            // untuk mendeteksi kapan objek masuk/keluar area trolley (trigger detection sistem).
            // areaTriggerCollider sudah di-cache di Awake — bukan GetComponent() lagi!
            if (areaTriggerCollider != null && cachedColliders[i] == areaTriggerCollider)
            {
                continue;
            }
            cachedColliders[i].enabled = enabled;
        }
    }

    /// <summary>
    /// Mengubah layer seluruh GameObject (root dan semua child) objek belanjaan ini.
    /// Layer diubah HANYA saat objek benar-benar diam (isKinematic=true, collider=false),
    /// bukan setiap frame, sehingga aman untuk Mobile WebGL.
    /// </summary>
    /// <param name="layer">Layer index tujuan (layerGoods atau layerGoodsInTrolley).</param>
    private void SetLayerRecursive(int layer)
    {
        if (layer == -1) return; // Guard: layer tidak valid, jangan ubah apapun

        // Ubah layer root object
        gameObject.layer = layer;

        // Ubah layer semua child secara rekursif agar detection bekerja konsisten
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            children[i].gameObject.layer = layer;
        }
    }

    /// <summary>
    /// Membangunkan Rigidbody secara fisik agar siap bertabrakan/bergerak (dipicu oleh ObjectAreaTrigger).
    /// </summary>
    public void WakeUp()
    {
        if (isPermanentlySleeping || rb == null) return;

        // KODENYA TERSPESIALISASI: Keluarkan dari parent trolley (ubah parent menjadi null) agar tidak mengganggu fisika pergerakan trolley saat barang bergerak bebas
        transform.SetParent(null);

        // Aktifkan kembali collider saat bergerak/jatuh
        SetCollidersEnabled(true);

        rb.isKinematic = false;
        rb.useGravity = true;
        isPhysicsActive = true;
        activeTime = 0f;

        // KODENYA TERSPESIALISASI: Ubah layer kembali ke Goods ketika barang dibangunkan/keluar dari trolley
        SetLayerRecursive(layerGoods);

        if (sleepMonitorCoroutine != null) StopCoroutine(sleepMonitorCoroutine);
        sleepMonitorCoroutine = StartCoroutine(PhysicsSleepMonitorCoroutine());
    }

    /// <summary>
    /// Coroutine pemantau berkala untuk mendeteksi apakah objek fisika sudah diam (tidur)
    /// guna menghemat overhead FixedUpdate di WebGL Mobile.
    /// </summary>
    private System.Collections.IEnumerator PhysicsSleepMonitorCoroutine()
    {
        // Caching delay untuk menghindari alokasi Garbage Collector di WebGL
        WaitForSeconds delay = new WaitForSeconds(sleepCheckInterval);

        while (isPhysicsActive && !isPermanentlySleeping && rb != null)
        {
            yield return delay;

            // Jika Rigidbody kinematic, berarti sedang dalam proses melayang/di-grab oleh HUDController.
            // Kita reset timer aktif dan tunggu sampai dilepaskan (menjadi non-kinematic).
            if (rb.isKinematic)
            {
                activeTime = 0f;
                continue;
            }

            activeTime += sleepCheckInterval;

            // Jika objek sudah berstatus Taken (di dalam trolley), tunggu sampai benar-benar diam/menetap, lalu kunci permanen
            if (currentStatus == ObjectStatus.Taken)
            {
                // Berikan sedikit toleransi waktu agar objek sempat jatuh bebas dan memantul sebelum dikunci
                // Menggunakan sqrMagnitude untuk menghindari operasi akar kuadrat (Mathf.Sqrt)
                if (activeTime > 0.3f && rb.velocity.sqrMagnitude < (objMoveThreshold * objMoveThreshold))
                {
                    SleepPermanently();
                }
            }
            else
            {
                // Untuk objek bebas di luar (Grounded): tidurkan kembali jika kecepatannya di bawah ambang batas
                // Berikan waktu minimal 0.2 detik sejak dibangunkan agar gaya luar sempat mempengaruhinya
                if (activeTime > 0.2f && rb.velocity.sqrMagnitude < (objMoveThreshold * objMoveThreshold))
                {
                    SleepGrounded();
                }
            }
        }
    }

    /// <summary>
    /// Menonaktifkan Rigidbody sementara agar hemat CPU saat objek kembali diam di luar trolley.
    /// </summary>
    private void SleepGrounded()
    {
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        isPhysicsActive = false;

        // KODENYA TERSPESIALISASI: Objek sudah diam di luar trolley (status = Grounded), ubah layer kembali ke Goods.
        // Layer Goods digunakan agar objek bisa berinteraksi normal dengan environment dan terdeteksi oleh sistem highlight.
        // Layer hanya diubah di sini (saat diam) — bukan di Update/FixedUpdate — agar aman di Mobile WebGL.
        SetLayerRecursive(layerGoods);
    }

    /// <summary>
    /// Menonaktifkan Rigidbody secara permanen setelah objek masuk ke dalam keranjang trolley dan diam.
    /// </summary>
    private void SleepPermanently()
    {
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        isPhysicsActive = false;
        isPermanentlySleeping = true;

        // Nonaktifkan trigger area agar tidak membebani komputasi physics trigger lagi
        if (areaTrigger != null)
        {
            areaTrigger.enabled = false;
        }

        // KODENYA TERSPESIALISASI: Jadikan child dari parent trolley agar bergerak mengikuti trolley
        if (trolleyParent != null)
        {
            transform.SetParent(trolleyParent);
        }

        // Matikan collider agar tidak bertabrakan dengan bodi trolley sendiri saat diam
        SetCollidersEnabled(false);

        // KODENYA TERSPESIALISASI: Objek sudah diam di dalam trolley (status = Taken), ubah layer ke GoodsInTrolley.
        // Layer GoodsInTrolley digunakan untuk memisahkan fisika objek dari layer Goods agar tidak saling menabrak
        // dan tidak terdeteksi oleh sistem TrolleyInteractController (menghemat CPU collision check).
        // Layer diubah DI SINI — hanya saat isKinematic=true dan collider=false — agar tidak ada overhead runtime.
        SetLayerRecursive(layerGoodsInTrolley);
    }

    // ==========================================
    // Properties Pendukung untuk Akses Eksternal
    // ==========================================

    /// <summary>
    /// Mengakses data template ScriptableObject secara langsung.
    /// </summary>
    public SupermarketObjectData ObjectData => objectData;

    /// <summary>
    /// Mengekspos Rigidbody yang sudah di-assign di Inspector sebagai property publik.
    /// OPTIMALISASI (BUG FIX #2): TrolleyAreaDetector.ShakeObjects TIDAK perlu memanggil GetComponent<Rigidbody>()
    /// lagi setiap tabrakan — cukup pakai ObjectScript.Rb yang sudah di-cache ini.
    /// </summary>
    public Rigidbody Rb => rb;

    /// <summary>
    /// Status aktif barang saat ini (bisa dirubah oleh trigger TrolleyArea atau genggaman Player).
    /// </summary>
    public ObjectStatus Status
    {
        get => currentStatus;
        set
        {
            currentStatus = value;
            if (currentStatus == ObjectStatus.Grounded)
            {
                // Jika dijatuhkan atau dilempar kembali, reset status tidur permanen agar bisa bangun-tidur lagi
                isPermanentlySleeping = false;
                if (areaTrigger != null)
                {
                    areaTrigger.enabled = true;
                }
                // Langsung bangunkan agar jatuh secara fisik ke tanah
                WakeUp();
            }
            else if (currentStatus == ObjectStatus.Taken)
            {
                // Saat masuk ke trolley, pastikan logic memproses peniduran permanen saat diam
                isPhysicsActive = true;
                activeTime = 0f;
                if (sleepMonitorCoroutine != null) StopCoroutine(sleepMonitorCoroutine);
                sleepMonitorCoroutine = StartCoroutine(PhysicsSleepMonitorCoroutine());
            }
        }
    }

    /// <summary>
    /// Berat barang bawaan. Diambil langsung dari data statis ScriptableObject.
    /// </summary>
    public float ObjWeight => objectData != null ? objectData.objWeight : 0f;

    /// <summary>
    /// Nama barang. Diambil langsung dari data statis ScriptableObject.
    /// </summary>
    public string ObjName => objectData != null ? objectData.objName : name;



    /// <summary>
    /// Membangunkan objek secara paksa dari goncangan/tabrakan trolley.
    /// Mengesampingkan status tidur permanen (isPermanentlySleeping) sementara waktu agar objek bisa bergerak dan memantul.
    /// </summary>
    public void WakeUpFromTrolleyHit()
    {
        isPermanentlySleeping = false;
        if (areaTrigger != null)
        {
            areaTrigger.enabled = true;
        }

        // KODENYA TERSPESIALISASI: Keluarkan dari parent trolley (ubah parent menjadi null) agar bebas bergerak/memantul saat tabrakan
        transform.SetParent(null);

        // Aktifkan kembali collider agar bisa memantul/tabrakan secara fisik
        SetCollidersEnabled(true);

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
        }

        // KODENYA TERSPESIALISASI (BUG FIX #3 — Layer saat barang melayang):
        // Saat barang dipaksa bangun dari goncangan trolley, rb dan collider sudah aktif.
        // Jika layernya masih GoodsInTrolley, collision detection dengan environment tidak akan bekerja.
        // Kita set ke Goods SEKARANG (saat rb aktif) agar fisika bekerja benar selama barang melayang.
        // Layer akan berganti ke GoodsInTrolley lagi hanya saat SleepPermanently() (saat benar-benar diam di trolley).
        SetLayerRecursive(layerGoods);

        isPhysicsActive = true;
        activeTime = 0f;
        if (sleepMonitorCoroutine != null) StopCoroutine(sleepMonitorCoroutine);
        sleepMonitorCoroutine = StartCoroutine(PhysicsSleepMonitorCoroutine());
    }
}
