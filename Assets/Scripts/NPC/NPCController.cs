using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Script ini mengontrol pergerakan fisik dan kecerdasan buatan (AI) dari Trolley NPC.
/// Menggunakan kombinasi NavMesh.CalculatePath untuk pencarian rute yang di-cache (tidak setiap frame),
/// dan kemudi berbasis fisika (Rigidbody) agar jalannya NPC setara dan konsisten dengan kontrol player.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class NPCController : MonoBehaviour
{
    public enum NPCType
    {
        YoungMan,    // Pergerakan cepat, berhenti lama di StayPoint untuk berpikir (hemat kosan).
        YoungWoman,  // Pergerakan lambat, durasi istirahat sedang, rute lorong tertentu.
        Mother,      // Pergerakan sangat cepat dan agresif, istirahat sangat singkat.
        Childish     // Kecepatan sedang-cepat, tidak stabil, istirahat sedang.
    }

    [Header("NPC Identity")]
    [Tooltip("Tipe NPC yang menentukan sifat pergerakan dan durasi berhentinya.")]
    [SerializeField] private NPCType npcType = NPCType.YoungMan;

    [Tooltip("Gunakan preset otomatis berdasarkan tipe NPC (jika false, nilai di Inspector di bawah ini yang akan dipakai).")]
    [SerializeField] private bool useDefaultPresets = true;

    [Tooltip("Preset konfigurasi lore/sifat NPC dari ScriptableObject. Jika diisi, ini akan digunakan untuk mengatur kecepatan dan penanganan NPC secara dinamis.")]
    [SerializeField] private NPCLorePreset lorePreset;

    [Header("Movement Configs (Manual Balancing)")]
    [Tooltip("Kecepatan maksimum NPC (dalam m/s).")]
    [SerializeField] private float maxSpeed = 5f;

    [Tooltip("Akselerasi pergerakan NPC untuk mencapai top speed.")]
    [SerializeField] private float acceleration = 8f;

    [Tooltip("Deselerasi pergerakan NPC ketika ingin berhenti.")]
    [SerializeField] private float deceleration = 10f;

    [Tooltip("Sensitivitas/Kecepatan putar bodi NPC saat membelok.")]
    [SerializeField] private float turnSensitivity = 2f;

    [Tooltip("Pengali kesusahan belok pada kecepatan maksimum (0-1). Semakin kecil, semakin kaku belok saat kencang.")]
    [Range(0.01f, 1f)]
    [SerializeField] private float turnDifficultyAtMaxSpeed = 0.3f;

    [Tooltip("Rasio threshold kecepatan (0-1) di mana belokan mulai terasa berat (seperti player).")]
    [Range(0.1f, 1f)]
    [SerializeField] private float heavyTurnSpeedThreshold = 0.75f;

    [Header("Reaction & KO Configs (Manual Balancing)")]
    [Tooltip("Batas kecepatan minimum Rigidbody senjata agar dianggap dilempar.")]
    [SerializeField] private float weaponVelocityThreshold = 1.5f;

    [Tooltip("Batas kecepatan tabrakan minimum troli player untuk memicu KO.")]
    [SerializeField] private float playerCollisionSpeedThreshold = 4.0f;

    [Tooltip("Gaya dorong balik horizontal saat terkena stun.")]
    [SerializeField] private float stunPushbackForce = 3.5f;

    [Tooltip("Komponen gaya dorong vertikal ke atas saat terkena stun.")]
    [SerializeField] private float stunUpwardFactor = 0.3f;

    [Tooltip("Gaya dorong vertikal ke atas saat ter-KO.")]
    [SerializeField] private float koUpwardForce = 4.0f;

    [Tooltip("Rentang gaya acak saat ter-KO.")]
    [SerializeField] private float koRandomForceRange = 2.0f;

    [Tooltip("Torsi/Putaran fisik saat ter-KO.")]
    [SerializeField] private float koTorqueForce = 12.0f;

    [Tooltip("Batas berat senjata ringan (kg) untuk stun durasi pendek.")]
    [SerializeField] private float lightWeaponWeightLimit = 0.5f;

    [Tooltip("Durasi stun singkat (detik) untuk senjata ringan.")]
    [SerializeField] private float lightStunDuration = 1.0f;

    [Tooltip("Batas berat senjata sedang (kg) untuk stun durasi menengah.")]
    [SerializeField] private float mediumWeaponWeightLimit = 1.0f;

    [Tooltip("Durasi stun sedang (detik) untuk senjata menengah.")]
    [SerializeField] private float mediumStunDuration = 2.0f;

    [Tooltip("Interval waktu (detik) untuk mereset rotasi X dan Z agar tetap 0. Set ke 0 untuk mereset setiap frame.")]
    [SerializeField] private float rotationResetInterval = 0.5f;

    [Header("Pathfinding & Waypoints (WebGL Mobile Optimization)")]
    [Tooltip("Daftar titik StayPoint (NPC akan berhenti diam di sini) yang di-assign langsung di Inspector.")]
    [SerializeField] private Transform[] stayPoints;

    [Tooltip("Daftar titik Waypoint biasa (NPC hanya lewat saja) yang di-assign langsung di Inspector.")]
    [SerializeField] private Transform[] normalWaypoints;

    [Tooltip("Durasi diam (detik) di StayPoint.")]
    [SerializeField] private float stayDuration = 5f;

    [Tooltip("Jarak toleransi (meter) untuk mendeteksi kedatangan di waypoint / titik sudut jalan.")]
    [SerializeField] private float waypointTolerance = 1.0f;

    [Tooltip("Jarak toleransi kedatangan (meter) sebelum NPC dianggap sampai di waypoint target.")]
    [SerializeField] private float arrivalDistanceThreshold = 1.0f;

    [Tooltip("Waktu maksimal (detik) tanpa pergerakan sebelum AI melakukan kalkulasi rute ulang (sistem pemulihan stuck).")]
    [SerializeField] private float stuckTimeout = 3.0f;

    [SerializeField] private string navMeshAgentName = "Trolley";

    [Header("Optimization Configs")]
    [Tooltip("Interval pembaruan kecerdasan AI navigasi (detik).")]
    [SerializeField] private float aiUpdateInterval = 0.5f;

    // Komponen referensi internal
    [SerializeField] private Rigidbody rb;
    private NavMeshPath navPath;

    // Cache WaitForSeconds untuk zero GC allocation di WebGL
    private WaitForSeconds aiUpdateDelay;
    private Vector3 currentMoveDirection = Vector3.zero;
    
    // Antrean rute patroli
    private List<Transform> waypointsList = new List<Transform>();
    private int currentWaypointIndex = 0;

    // Cache koordinat sudut rute (Path Caching - Menghindari per-frame pathfinding)
    private Vector3[] pathCorners;
    private int currentCornerIndex = 0;

    // Cache kuadrat toleransi untuk efisiensi perbandingan tanpa Sqrt (Mobile WebGL optimization)
    private float sqrWaypointTolerance;
    
    // Cache kuadrat toleransi kedatangan untuk efisiensi perbandingan tanpa Sqrt (Mobile WebGL optimization)
    private float sqrArrivalDistanceThreshold;

    // Set untuk lookup O(1) cepat apakah suatu waypoint adalah StayPoint (WebGL Mobile optimization)
    private HashSet<Transform> stayPointsSet = new HashSet<Transform>();

    // Query filter untuk menentukan Agent Type ID saat kalkulasi rute NavMesh
    private NavMeshQueryFilter queryFilter;

    // Variabel penampung range acak berdasarkan GDD (Game Design Document)
    private float activeMinSpeed;
    private float activeMaxSpeed;
    private float activeMinStay;
    private float activeMaxStay;

    // Status pergerakan & waktu tunggu
    private float currentForwardSpeed = 0f;
    private bool isWaiting = false;
    private float waitTimer = 0f;

    // Status KO (Knockout) akibat dilempar senjata atau ditabrak keras oleh player
    private bool isKO = false;

    // Status Stun sementara akibat terkena lemparan senjata ringan/sedang
    private bool isStunned = false;
    private float stunTimer = 0f;

    // Deteksi stuck/terjebak fisik
    private Vector3 lastPosition;
    private float stuckTimer = 0f;
    private int stuckRecalculateCount = 0;

    private void Start()
    {
        // Pastikan rigidbody disetel dengan aman untuk mobile WebGL (mengunci rotasi agar tidak guling)
        if (rb != null)
        {
            rb.drag = 0.5f;
            rb.angularDrag = 0.5f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        // Kumpulkan semua waypoint di bawah root transform
        InitializeWaypoints();

        // Terapkan penyesuaian preset lore NPC jika diaktifkan
        if (useDefaultPresets)
        {
            ApplyLorePreset();
        }

        // Inisialisasi posisi terakhir untuk deteksi stuck awal
        lastPosition = transform.position;

        // Hitung kuadrat toleransi agar terhindar dari operasi akar (Sqrt) di FixedUpdate
        sqrWaypointTolerance = waypointTolerance * waypointTolerance;
        sqrArrivalDistanceThreshold = arrivalDistanceThreshold * arrivalDistanceThreshold;

        // Mengambil Agent Type ID berdasarkan konfigurasi nama di Navigation settings Unity
        int agentTypeId = 0; // Default Humanoid
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
        {
            var settings = NavMesh.GetSettingsByIndex(i);
            if (NavMesh.GetSettingsNameFromID(settings.agentTypeID) == navMeshAgentName)
            {
                agentTypeId = settings.agentTypeID;
                break;
            }
        }

        // Setup filter NavMesh Query untuk jalur pathfinding
        queryFilter = new NavMeshQueryFilter();
        queryFilter.areaMask = NavMesh.AllAreas;
        queryFilter.agentTypeID = agentTypeId;

        // Mulai jalankan rute awal ke waypoint pertama
        if (waypointsList.Count > 0)
        {
            CalculatePathToTarget(waypointsList[currentWaypointIndex].position);
        }

        // Jalankan Coroutine mandiri untuk menjaga agar NPC tetap tegak lurus (X/Z = 0) saat patroli
        StartCoroutine(KeepUprightCoroutine());

        // Inisialisasi delay AI dan jalankan loop AI secara berkala (recursive coroutine)
        aiUpdateDelay = new WaitForSeconds(aiUpdateInterval);
        StartCoroutine(AILoop());
    }

    private void FixedUpdate()
    {
        // OPTIMALISASI FISIKA WEBGL:
        // Jika NPC sedang dalam kondisi KO (Knockout), hentikan seluruh logika AI dan navigasi
        // agar tidak membebani komputasi CPU dan membiarkan simulasi fisika ragdoll berjalan penuh.
        if (isKO) return;

        // Jika sedang terkena efek Stun, jalankan jeda waktu dan rem troli NPC
        if (isStunned)
        {
            stunTimer -= Time.fixedDeltaTime;
            DecelerateTrolley(); // Mengerem troli secara perlahan (hemat CPU)

            if (stunTimer <= 0f)
            {
                isStunned = false;
#if UNITY_EDITOR
                Debug.Log($"[NPCController] NPC '{gameObject.name}' pulih dari efek Stun.");
#endif
            }
            return; // Lewati sisa logika navigasi AI saat pusing/stun
        }

        // 1. Kondisi diam/menunggu di StayPoint
        if (isWaiting)
        {
            waitTimer -= Time.fixedDeltaTime;
            
            // Perlahan rem trolley NPC hingga diam
            DecelerateTrolley();

            if (waitTimer <= 0f)
            {
                isWaiting = false;
                AdvanceToNextWaypoint();
            }
            return;
        }

        // 2. Kemudikan trolley secara fisik menggunakan arah pergerakan ter-cache
        if (currentMoveDirection.sqrMagnitude > 0.001f)
        {
            SteerPhysicsTrolley(currentMoveDirection);
        }
        else
        {
            DecelerateTrolley();
        }
    }

    /// <summary>
    /// Loop rekursif berkala untuk memproses kecerdasan AI navigasi dan deteksi stuck (hemat CPU).
    /// </summary>
    private System.Collections.IEnumerator AILoop()
    {
        if (isKO) yield break;

        if (!isStunned && !isWaiting && waypointsList.Count > 0)
        {
            Transform targetWaypoint = waypointsList[currentWaypointIndex];

            // Deteksi kedatangan di waypoint utama (squared magnitude)
            float sqrDistanceToWaypoint = (transform.position - targetWaypoint.position).sqrMagnitude;
            if (sqrDistanceToWaypoint <= sqrArrivalDistanceThreshold)
            {
                if (IsStayPoint(targetWaypoint))
                {
                    isWaiting = true;
                    float currentStayDuration = useDefaultPresets ? Random.Range(activeMinStay, activeMaxStay) : stayDuration;
                    waitTimer = currentStayDuration;
#if UNITY_EDITOR
                    Debug.Log($"[NPCController] NPC '{gameObject.name}' sampai di StayPoint '{targetWaypoint.name}'. Berhenti selama {currentStayDuration:F1} detik.");
#endif
                }
                else
                {
                    AdvanceToNextWaypoint();
                }
            }
            else
            {
                // Proses pembaruan arah sudut rute dan stuck check
                UpdatePathFollowing();
            }
        }

        yield return aiUpdateDelay;
        StartCoroutine(AILoop());
    }

    /// <summary>
    /// Memperbarui arah navigasi berdasarkan cache sudut rute (corners) dan mendeteksi kondisi stuck.
    /// </summary>
    private void UpdatePathFollowing()
    {
        if (pathCorners == null || pathCorners.Length == 0 || currentCornerIndex >= pathCorners.Length)
        {
            Transform targetWaypoint = waypointsList[currentWaypointIndex];
            CalculatePathToTarget(targetWaypoint.position);
            return;
        }

        Vector3 targetCorner = pathCorners[currentCornerIndex];

        // Deteksi kedatangan di sudut rute saat ini
        float sqrDistanceToCorner = (transform.position - targetCorner).sqrMagnitude;
        if (sqrDistanceToCorner <= sqrWaypointTolerance)
        {
            currentCornerIndex++;
            stuckRecalculateCount = 0;
            if (currentCornerIndex >= pathCorners.Length)
            {
                currentMoveDirection = Vector3.zero;
                return;
            }
            targetCorner = pathCorners[currentCornerIndex];
        }

        // Deteksi Stuck (kalkulasi jarak tempuh sejak interval pembaruan terakhir)
        float sqrDistanceMoved = (transform.position - lastPosition).sqrMagnitude;
        if (sqrDistanceMoved < 0.0025f) // Bergerak kurang dari 5 cm dalam interval update
        {
            stuckTimer += aiUpdateInterval;
            if (stuckTimer >= stuckTimeout)
            {
                stuckRecalculateCount++;
                if (stuckRecalculateCount >= 2)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"[NPCController] NPC '{gameObject.name}' tetap terjebak setelah kalkulasi ulang. Melewati waypoint '{waypointsList[currentWaypointIndex].name}'.");
#endif
                    stuckRecalculateCount = 0;
                    stuckTimer = 0f;
                    AdvanceToNextWaypoint();
                    return;
                }

#if UNITY_EDITOR
                Debug.LogWarning($"[NPCController] NPC '{gameObject.name}' terjebak (stuck)! Melakukan recalculate rute.");
#endif
                stuckTimer = 0f;
                Transform targetWaypoint = waypointsList[currentWaypointIndex];
                CalculatePathToTarget(targetWaypoint.position);
                return;
            }
        }
        else
        {
            stuckTimer = 0f;
            stuckRecalculateCount = 0;
        }
        lastPosition = transform.position;

        // Hitung arah gerak ke target corner berikutnya
        Vector3 moveDirection = (targetCorner - transform.position);
        moveDirection.y = 0f;

        if (moveDirection.sqrMagnitude > 0.0001f)
        {
            currentMoveDirection = moveDirection.normalized;
        }
        else
        {
            currentMoveDirection = Vector3.zero;
        }
    }

    /// <summary>
    /// Menginisialisasi rute patroli dengan memasukkan waypoint dari array StayPoints dan NormalWaypoints.
    /// Menggunakan AddRange untuk penggabungan (concatenation) instan yang sangat efisien di level memori C#.
    /// </summary>
    private void InitializeWaypoints()
    {
        waypointsList.Clear();
        stayPointsSet.Clear();

        // Menggabungkan array StayPoints ke dalam list secara instan
        if (stayPoints != null)
        {
            waypointsList.AddRange(stayPoints);

            // Daftarkan ke HashSet untuk lookup status staypoint yang sangat cepat (O(1))
            for (int i = 0; i < stayPoints.Length; i++)
            {
                if (stayPoints[i] != null)
                {
                    stayPointsSet.Add(stayPoints[i]);
                }
            }
        }

        // Menggabungkan array NormalWaypoints ke dalam list secara instan
        if (normalWaypoints != null)
        {
            waypointsList.AddRange(normalWaypoints);
        }

        // Bersihkan elemen kosong (null) jika ada slot kosong di Inspector dalam satu pass cepat
        waypointsList.RemoveAll(item => item == null);

        if (waypointsList.Count == 0)
        {
            Debug.LogWarning($"[NPCController] Tidak ada waypoint (StayPoints / NormalWaypoints) yang di-assign pada NPC: '{gameObject.name}'!");
        }
    }

    /// <summary>
    /// Menentukan tipe waypoint berdasarkan HashSet stayPointsSet (WebGL Mobile optimization).
    /// </summary>
    private bool IsStayPoint(Transform waypoint)
    {
        return waypoint != null && stayPointsSet.Contains(waypoint);
    }

    /// <summary>
    /// Memajukan rute NPC ke target waypoint berikutnya secara memutar (looping).
    /// </summary>
    private void AdvanceToNextWaypoint()
    {
        if (waypointsList.Count == 0) return;

        // Pilih target berikutnya secara acak, bukan urut (looping)
        if (waypointsList.Count > 1)
        {
            int nextIndex;
            do
            {
                nextIndex = Random.Range(0, waypointsList.Count);
            } while (nextIndex == currentWaypointIndex);
            currentWaypointIndex = nextIndex;
        }
        else
        {
            currentWaypointIndex = 0;
        }

        Transform nextWaypoint = waypointsList[currentWaypointIndex];

        // LOGIC DI BALIK LAYAR: Acak kecepatan untuk jalur berikutnya agar pergerakan terasa dinamis dan "chaos"
        if (useDefaultPresets)
        {
            maxSpeed = Random.Range(activeMinSpeed, activeMaxSpeed);
        }

        // Hitung rute baru ke waypoint selanjutnya
        CalculatePathToTarget(nextWaypoint.position);
    }

    /// <summary>
    /// Menghitung rute NavMesh dan menyimpannya ke dalam cache (corners).
    /// Menggunakan NavMesh.SamplePosition agar rute tetap berhasil ditemukan meskipun posisi awal atau target sedikit offset dari NavMesh.
    /// </summary>
    private void CalculatePathToTarget(Vector3 targetPosition)
    {
        if (navPath == null)
        {
            navPath = new NavMeshPath();
        }

        Vector3 sourcePosition = transform.position;
        NavMeshHit hitStart;
        // Coba cari posisi terdekat dengan queryFilter (agent type khusus) dan toleransi lebih besar (15.0f)
        if (NavMesh.SamplePosition(transform.position, out hitStart, 15.0f, queryFilter))
        {
            sourcePosition = hitStart.position;
        }
        else if (NavMesh.SamplePosition(transform.position, out hitStart, 15.0f, NavMesh.AllAreas))
        {
            // Fallback jika agent type khusus belum di-bake di Navigation settings
            sourcePosition = hitStart.position;
        }

        Vector3 destPosition = targetPosition;
        NavMeshHit hitEnd;
        if (NavMesh.SamplePosition(targetPosition, out hitEnd, 15.0f, queryFilter))
        {
            destPosition = hitEnd.position;
        }
        else if (NavMesh.SamplePosition(targetPosition, out hitEnd, 15.0f, NavMesh.AllAreas))
        {
            // Fallback jika agent type khusus belum di-bake di Navigation settings
            destPosition = hitEnd.position;
        }

        // PENTING UNTUK WEBGL: NavMesh.CalculatePath adalah metode static yang sangat efisien 
        // karena tidak memerlukan overhead update per-frame dari komponen NavMeshAgent.
        NavMesh.CalculatePath(sourcePosition, destPosition, queryFilter, navPath);

        // Fallback jika kalkulasi dengan queryFilter gagal (misal agent type khusus belum di-bake)
        if (navPath.status == NavMeshPathStatus.PathInvalid)
        {
            NavMesh.CalculatePath(sourcePosition, destPosition, NavMesh.AllAreas, navPath);
        }

        if (navPath.status == NavMeshPathStatus.PathComplete || navPath.status == NavMeshPathStatus.PathPartial)
        {
            pathCorners = navPath.corners;
            currentCornerIndex = 0;

            // Lewati titik index 0 karena itu biasanya adalah titik start (posisi NPC saat ini)
            if (pathCorners != null && pathCorners.Length > 1)
            {
                currentCornerIndex = 1;
            }
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[NPCController] NPC '{gameObject.name}' gagal mendapatkan jalur dari {sourcePosition} ke {destPosition}. PathStatus: {navPath.status}");
#endif
            pathCorners = null;
        }

        // Reset pelacak stuck
        stuckTimer = 0f;
        lastPosition = transform.position;
    }

    /// <summary>
    /// Menggerakkan fisik Rigidbody menyusuri cache corners rute.
    /// </summary>
    private void FollowPath()
    {
        // Jika cache rute kosong, minta hitung ulang
        if (pathCorners == null || pathCorners.Length == 0 || currentCornerIndex >= pathCorners.Length)
        {
            Transform targetWaypoint = waypointsList[currentWaypointIndex];
            CalculatePathToTarget(targetWaypoint.position);
            return;
        }

        Vector3 targetCorner = pathCorners[currentCornerIndex];

        // 1. Deteksi kedatangan di sudut rute saat ini (Menggunakan sqrMagnitude untuk efisiensi eksekusi WebGL)
        float sqrDistanceToCorner = (transform.position - targetCorner).sqrMagnitude;
        if (sqrDistanceToCorner <= sqrWaypointTolerance)
        {
            currentCornerIndex++;
            stuckRecalculateCount = 0; // Reset stuck counter saat sukses mencapai sudut baru
            if (currentCornerIndex >= pathCorners.Length)
            {
                // Selesai menyusuri rute sudut, biarkan FixedUpdate mendeteksi kedatangan di waypoint utama
                return;
            }
            targetCorner = pathCorners[currentCornerIndex];
        }

        // 2. Deteksi Stuck (Pemulihan jika terhalang dinding/player/obstacle)
        // Gunakan kuadrat jarak (0.05f * 0.05f = 0.0025f) untuk efisiensi CPU WebGL
        float sqrDistanceMoved = (transform.position - lastPosition).sqrMagnitude;
        if (sqrDistanceMoved < 0.0025f) // Bergerak sangat lambat atau diam
        {
            stuckTimer += Time.fixedDeltaTime;
            if (stuckTimer >= stuckTimeout)
            {
                stuckRecalculateCount++;
                if (stuckRecalculateCount >= 2)
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"[NPCController] NPC '{gameObject.name}' tetap terjebak setelah kalkulasi ulang. Melewati waypoint '{waypointsList[currentWaypointIndex].name}'.");
#endif
                    stuckRecalculateCount = 0;
                    stuckTimer = 0f;
                    AdvanceToNextWaypoint();
                    return;
                }

#if UNITY_EDITOR
                Debug.LogWarning($"[NPCController] NPC '{gameObject.name}' terjebak (stuck)! Melakukan recalculate rute.");
#endif
                stuckTimer = 0f;
                Transform targetWaypoint = waypointsList[currentWaypointIndex];
                CalculatePathToTarget(targetWaypoint.position);
                return;
            }
        }
        else
        {
            stuckTimer = 0f; // Reset timer jika masih bisa bergerak
            stuckRecalculateCount = 0; // Reset counter
        }
        lastPosition = transform.position;

        // 3. Hitung arah ke sudut target berikutnya
        Vector3 moveDirection = (targetCorner - transform.position);
        moveDirection.y = 0f; // Abaikan perbedaan tinggi vertikal

        if (moveDirection.magnitude > 0.01f)
        {
            moveDirection.Normalize();
            SteerPhysicsTrolley(moveDirection);
        }
    }

    /// <summary>
    /// Mengemudikan bodi Rigidbody NPC secara fisik mengikuti parameter setara Player.
    /// </summary>
    private void SteerPhysicsTrolley(Vector3 moveDirection)
    {
        // 1. Tentukan pengali reduksi rotasi berdasarkan rasio kecepatan (Need for Seat style)
        // Hitung speed magnitude secara manual tanpa instansiasi Vector3 baru untuk meminimalkan alokasi stack/CPU
        float speedSqr = rb.velocity.x * rb.velocity.x + rb.velocity.z * rb.velocity.z;
        float currentSpeed = speedSqr > 0.0001f ? Mathf.Sqrt(speedSqr) : 0f;
        float speedRatio = Mathf.Clamp01(currentSpeed / maxSpeed);

        float speedTurnMultiplier;
        if (speedRatio >= heavyTurnSpeedThreshold)
        {
            speedTurnMultiplier = turnDifficultyAtMaxSpeed;
        }
        else
        {
            float normalizedRatio = speedRatio / heavyTurnSpeedThreshold;
            speedTurnMultiplier = Mathf.Lerp(1f, turnDifficultyAtMaxSpeed, normalizedRatio);
        }

        // 2. Putar bodi secara halus menuju arah target sudut
        Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
        
        // Menentukan besaran rotasi frame ini (100f adalah pengali kecocokan skala putaran)
        float rotationStep = turnSensitivity * speedTurnMultiplier * 100f * Time.fixedDeltaTime;
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRotation, rotationStep));

        // 3. Terapkan akselesari pergerakan maju
        // Hanya dorong maju jika orientasi hadapan NPC sudah mengarah ke sudut target (mencegah trolley meluncur miring)
        float facingDot = Vector3.Dot(transform.forward, moveDirection);
        float targetForwardSpeed = 0f;

        if (facingDot > 0.2f)
        {
            targetForwardSpeed = maxSpeed;
        }

        // Terapkan akselerasi/deselerasi linear secara persistent
        if (targetForwardSpeed > 0f)
        {
            currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, targetForwardSpeed, acceleration * Time.fixedDeltaTime);
        }
        else
        {
            currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, 0f, deceleration * Time.fixedDeltaTime);
        }

        // Pindahkan bodi menggunakan Rigidbody.velocity dengan mempertahankan gaya gravitasi Y
        Vector3 localVelocity = transform.forward * currentForwardSpeed;
        rb.velocity = new Vector3(localVelocity.x, rb.velocity.y, localVelocity.z);
    }

    /// <summary>
    /// Mengerem trolley secara perlahan hingga berhenti total.
    /// </summary>
    private void DecelerateTrolley()
    {
        currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, 0f, deceleration * Time.fixedDeltaTime);
        Vector3 localVelocity = transform.forward * currentForwardSpeed;
        rb.velocity = new Vector3(localVelocity.x, rb.velocity.y, localVelocity.z);
    }

    /// <summary>
    /// Konfigurasi default sifat/lore NPC secara otomatis saat inisialisasi awal.
    /// </summary>
    private void ApplyLorePreset()
    {
        if (lorePreset != null)
        {
            // Ambil konfigurasi dinamis dari ScriptableObject yang di-assign
            activeMinSpeed = lorePreset.activeMinSpeed;
            activeMaxSpeed = lorePreset.activeMaxSpeed;
            activeMinStay = lorePreset.activeMinStay;
            activeMaxStay = lorePreset.activeMaxStay;
            acceleration = lorePreset.acceleration;
            deceleration = lorePreset.deceleration;
            turnSensitivity = lorePreset.turnSensitivity;
        }
        else
        {
            // Fallback manual bawaan jika ScriptableObject kosong agar game tetap berjalan stabil
            switch (npcType)
            {
                case NPCType.YoungMan:
                    // Young Man: Kecepatan random 8-9 m/s, Stay random 5-6 detik.
                    activeMinSpeed = 8.0f;
                    activeMaxSpeed = 9.0f;
                    activeMinStay = 5.0f;
                    activeMaxStay = 6.0f;
                    acceleration = 10f;
                    deceleration = 12f;
                    turnSensitivity = 2.0f;
                    break;

                case NPCType.YoungWoman:
                    // Young Woman: Kecepatan random 5-6 m/s, Stay random 5-6 detik.
                    activeMinSpeed = 5.0f;
                    activeMaxSpeed = 6.0f;
                    activeMinStay = 5.0f;
                    activeMaxStay = 6.0f;
                    acceleration = 6f;
                    deceleration = 8f;
                    turnSensitivity = 1.5f;
                    break;

                case NPCType.Mother:
                    // Mother: Kecepatan random 9-10 m/s, Stay random 3-4 detik.
                    activeMinSpeed = 9.0f;
                    activeMaxSpeed = 10.0f;
                    activeMinStay = 3.0f;
                    activeMaxStay = 4.0f;
                    acceleration = 16f;
                    deceleration = 20f;
                    turnSensitivity = 2.5f;
                    break;

                case NPCType.Childish:
                    // Childish: Kecepatan random 6-7 m/s, Kelelahan diam cukup lama (6-7 detik).
                    activeMinSpeed = 6.0f;
                    activeMaxSpeed = 7.0f;
                    activeMinStay = 6.0f;
                    activeMaxStay = 7.0f;
                    acceleration = 8f;
                    deceleration = 10f;
                    turnSensitivity = 2.2f;
                    break;
            }
        }

        // Setel kecepatan awal secara acak
        maxSpeed = Random.Range(activeMinSpeed, activeMaxSpeed);
    }

    /// <summary>
    /// Mendeteksi benturan fisik dari luar (tabrakan troli player atau senjata lempar).
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        if (isKO) return;

        // 1. KONDISI A: Tertabrak barang belanjaan/senjata yang dilempar oleh Player
        ObjectScript objScript = collision.gameObject.GetComponent<ObjectScript>();
        if (objScript == null) objScript = collision.gameObject.GetComponentInParent<ObjectScript>();
        if (objScript == null) objScript = collision.gameObject.GetComponentInChildren<ObjectScript>();

        if (objScript != null)
        {
            Rigidbody weaponRb = collision.gameObject.GetComponent<Rigidbody>();
            if (weaponRb == null) weaponRb = collision.gameObject.GetComponentInParent<Rigidbody>();
            if (weaponRb == null) weaponRb = collision.gameObject.GetComponentInChildren<Rigidbody>();

            // Pastikan senjata tersebut sedang bergerak cepat/dilempar (kecepatan > threshold)
            // Ini untuk menyaring senjata yang diam atau sekadar bergeser pelan di lantai.
            if (weaponRb != null && weaponRb.velocity.magnitude > weaponVelocityThreshold)
            {
                float weight = objScript.ObjWeight;
                
                // LOGIC BERDASARKAN BERAT BARANG (GDD):
                // - Berat <= lightWeaponWeightLimit: Stun selama lightStunDuration.
                // - Berat <= mediumWeaponWeightLimit: Stun selama mediumStunDuration.
                // - Berat diatas itu: KO langsung (diam selamanya).
                if (weight <= lightWeaponWeightLimit)
                {
                    TriggerStun(lightStunDuration, collision, weight);
                }
                else if (weight <= mediumWeaponWeightLimit)
                {
                    TriggerStun(mediumStunDuration, collision, weight);
                }
                else
                {
                    TriggerKO();
                }
                return;
            }
        }

        // 2. KONDISI B: Ditabrak paksa oleh Trolley Player pada kecepatan tinggi
        TrolleyController trolley = collision.gameObject.GetComponent<TrolleyController>();
        if (trolley == null) trolley = collision.gameObject.GetComponentInParent<TrolleyController>();
        if (trolley == null) trolley = collision.gameObject.GetComponentInChildren<TrolleyController>();

        // Pastikan objek yang menabrak memiliki TrolleyController tetapi bukan dikendalikan oleh NPC (berarti player)
        if (trolley != null)
        {
            NPCController otherNpc = collision.gameObject.GetComponent<NPCController>();
            if (otherNpc == null) otherNpc = collision.gameObject.GetComponentInParent<NPCController>();
            if (otherNpc == null) otherNpc = collision.gameObject.GetComponentInChildren<NPCController>();

            if (otherNpc == null)
            {
                Rigidbody playerRb = collision.gameObject.GetComponent<Rigidbody>();
                if (playerRb == null) playerRb = collision.gameObject.GetComponentInParent<Rigidbody>();
                if (playerRb == null) playerRb = collision.gameObject.GetComponentInChildren<Rigidbody>();

                // Kecepatan minimal troli player untuk memicu KO adalah playerCollisionSpeedThreshold m/s
                if (playerRb != null && playerRb.velocity.magnitude > playerCollisionSpeedThreshold)
                {
                    TriggerKO();
                }
            }
        }
    }

    /// <summary>
    /// Memicu status Stun sementara pada NPC.
    /// NPC akan terhenti, AI dinonaktifkan sementara, dan bodi terdorong pusing.
    /// </summary>
    private void TriggerStun(float duration, Collision collision, float weight)
    {
        isStunned = true;
        stunTimer = duration;
        isWaiting = false; // Batalkan status tunggu staypoint normal jika sedang terhuyung

#if UNITY_EDITOR
        Debug.Log($"[NPCController] NPC '{gameObject.name}' Ter-Stun {duration} detik! (Berat Senjata: {weight} kg)");
#endif

        // Berikan gaya tolak (feedback fisik ringan) agar NPC tampak terhuyung saat terkena hantaman
        if (rb != null)
        {
            Vector3 pushDirection = (transform.position - collision.transform.position).normalized;
            pushDirection.y = stunUpwardFactor; // Dorong sedikit ke atas agar efeknya lebih terasa
            rb.AddForce(pushDirection.normalized * stunPushbackForce, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// Memicu status Knockout (KO) pada NPC, menghentikan kecerdasan buatan, dan melepaskan kendali fisika.
    /// </summary>
    private void TriggerKO()
    {
        isKO = true;
        isStunned = false; // Pastikan status stun mati karena KO adalah status permanen tingkat akhir

#if UNITY_EDITOR
        Debug.Log($"[NPCController] NPC '{gameObject.name}' Ter-KO!");
#endif

        // Laporkan KO ke ScoreManager untuk penambahan poin
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.RegisterNpcKO();
        }

        // Reset status tunggu AI
        isWaiting = false;
        
        // OPTIMALISASI FISIKA WEBGL:
        // Mematikan batasan rotasi Rigidbody agar troli dan NPC dapat terguling jatuh secara alami.
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.None; // Bebaskan FreezeRotationX dan FreezeRotationZ
            
            // Terapkan gaya kejut (impulse force) acak untuk efek visual terpental yang memuaskan (Wow Effect)
            Vector3 bounceDirection = Vector3.up * koUpwardForce + Random.onUnitSphere * koRandomForceRange;
            rb.AddForce(bounceDirection, ForceMode.Impulse);
            rb.AddTorque(Random.onUnitSphere * koTorqueForce, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// Coroutine mandiri untuk menjaga agar NPC tetap tegak lurus (rotation X & Z = 0) saat patroli.
    /// Hanya aktif jika NPC belum dalam kondisi KO.
    /// </summary>
    private System.Collections.IEnumerator KeepUprightCoroutine()
    {
        // Cache yield instruction untuk mencegah alokasi GC (Garbage Collection) di Mobile WebGL
        WaitForSeconds delay = rotationResetInterval > 0f ? new WaitForSeconds(rotationResetInterval) : null;

        while (true)
        {
            if (isKO) yield break; // Jika sudah KO, matikan coroutine ini selamanya agar bisa terguling bebas

            ResetRotationXZ();

            if (rotationResetInterval > 0f)
            {
                yield return delay;
            }
            else
            {
                yield return null; // Jika interval disetel <= 0, reset berjalan setiap frame (smooth)
            }
        }
    }

    /// <summary>
    /// Mereset rotasi X dan Z dari transform menjadi tepat 0 secara efisien.
    /// Hanya melakukan write ke transform jika ada deviasi sudut (menghindari CPU overhead di WebGL).
    /// </summary>
    private void ResetRotationXZ()
    {
        Vector3 currentRot = transform.eulerAngles;
        // PENTING: Gunakan Mathf.Approximately untuk mengecek deviasi float secara cepat tanpa overhead
        if (!Mathf.Approximately(currentRot.x, 0f) || !Mathf.Approximately(currentRot.z, 0f))
        {
            currentRot.x = 0f;
            currentRot.z = 0f;
            transform.eulerAngles = currentRot;

            // Jika Rigidbody masih memiliki sisa kecepatan sudut miring, hentikan agar rotasi fisik sinkron
            if (rb != null && !rb.isKinematic)
            {
                Vector3 angularVel = rb.angularVelocity;
                angularVel.x = 0f;
                angularVel.z = 0f;
                rb.angularVelocity = angularVel;
            }
        }
    }
}
