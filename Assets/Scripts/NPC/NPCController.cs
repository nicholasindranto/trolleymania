using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Script ini mengontrol pergerakan kecerdasan buatan (AI) dari Trolley NPC menggunakan NavMeshAgent murni.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
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

#pragma warning disable 0414
    [Tooltip("Pengali kesusahan belok pada kecepatan maksimum (0-1). Semakin kecil, semakin kaku belok saat kencang.")]
    [Range(0.01f, 1f)]
    [SerializeField] private float turnDifficultyAtMaxSpeed = 0.3f;

    [Tooltip("Rasio threshold kecepatan (0-1) di mana belokan mulai terasa berat (seperti player).")]
    [Range(0.1f, 1f)]
    [SerializeField] private float heavyTurnSpeedThreshold = 0.75f;
#pragma warning restore 0414

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

#pragma warning disable 0414
    [SerializeField] private string navMeshAgentName = "Trolley";
#pragma warning restore 0414

    [Header("Optimization Configs")]
    [Tooltip("Interval pembaruan kecerdasan AI navigasi (detik).")]
    [SerializeField] private float aiUpdateInterval = 0.5f;

    [Tooltip("Interval pembaruan status timer (Stun & Wait) NPC (detik).")]
    [SerializeField] private float statusUpdateInterval = 0.1f;

    // Komponen referensi internal
    [SerializeField] private Rigidbody rb;
    private NavMeshAgent agent;

    // Cache WaitForSeconds untuk zero GC allocation di WebGL
    private WaitForSeconds aiUpdateDelay;
    private WaitForSeconds statusUpdateDelay;
    
    // Antrean rute patroli
    private List<Transform> waypointsList = new List<Transform>();
    private int currentWaypointIndex = 0;

    // Cache kuadrat toleransi untuk efisiensi perbandingan tanpa Sqrt (Mobile WebGL optimization)
    private float sqrWaypointTolerance;
    
    // Cache kuadrat toleransi kedatangan untuk efisiensi perbandingan tanpa Sqrt (Mobile WebGL optimization)
    private float sqrArrivalDistanceThreshold;

    // Set untuk lookup O(1) cepat apakah suatu waypoint adalah StayPoint (WebGL Mobile optimization)
    private HashSet<Transform> stayPointsSet = new HashSet<Transform>();

    // Variabel penampung range acak berdasarkan GDD (Game Design Document)
    private float activeMinSpeed;
    private float activeMaxSpeed;
    private float activeMinStay;
    private float activeMaxStay;

    // Status pergerakan & waktu tunggu
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

    public float CurrentSpeed => (agent != null && agent.enabled) ? agent.velocity.magnitude : 0f;
    public float MaxSpeed => maxSpeed;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        // Pastikan rigidbody disetel dengan aman untuk mobile WebGL (mengunci rotasi agar tidak guling) jika ada
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
        else
        {
            if (agent != null)
            {
                agent.speed = maxSpeed;
                agent.acceleration = acceleration;
                agent.angularSpeed = turnSensitivity * 100f;
                agent.stoppingDistance = arrivalDistanceThreshold;
            }
        }

        // Inisialisasi posisi terakhir untuk deteksi stuck awal
        lastPosition = transform.position;

        // Hitung kuadrat toleransi agar terhindar dari operasi akar (Sqrt)
        sqrWaypointTolerance = waypointTolerance * waypointTolerance;
        sqrArrivalDistanceThreshold = arrivalDistanceThreshold * arrivalDistanceThreshold;

        // Mulai jalankan rute awal ke waypoint pertama
        if (waypointsList.Count > 0)
        {
            if (agent != null && agent.enabled)
            {
                agent.SetDestination(waypointsList[currentWaypointIndex].position);
                agent.isStopped = false;
            }
        }

        // Inisialisasi delay AI dan jalankan loop AI secara berkala (recursive coroutine)
        aiUpdateDelay = new WaitForSeconds(aiUpdateInterval);
        statusUpdateDelay = new WaitForSeconds(statusUpdateInterval);
        StartCoroutine(AILoop());
        StartCoroutine(StatusUpdateLoop());
    }

    private System.Collections.IEnumerator StatusUpdateLoop()
    {
        while (true)
        {
            // KODENYA TERSPESIALISASI: Jika game masih loading, rem trolley NPC dan matikan logika
            if (ObjectiveManager.IsLoading)
            {
                if (agent != null && agent.enabled)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }
                yield return statusUpdateDelay;
                continue;
            }

            // Jika NPC sedang dalam kondisi KO (Knockout), hentikan seluruh logika AI dan navigasi
            if (isKO)
            {
                if (agent != null && agent.enabled)
                {
                    agent.enabled = false;
                }
                yield break; // Hentikan coroutine ini secara permanen
            }

            // Jika sedang terkena efek Stun, jalankan jeda waktu dan rem troli NPC
            if (isStunned)
            {
                stunTimer -= statusUpdateInterval;
                if (agent != null && agent.enabled)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }

                if (stunTimer <= 0f)
                {
                    isStunned = false;
                    if (rb != null)
                    {
                        rb.isKinematic = true;
                        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                    }
                    transform.eulerAngles = new Vector3(0f, transform.eulerAngles.y, 0f);

                    if (agent != null && agent.enabled)
                    {
                        agent.isStopped = false;
                    }
#if UNITY_EDITOR
                    Debug.Log($"[NPCController] NPC '{gameObject.name}' pulih dari efek Stun.");
#endif
                }
            }
            // Kondisi diam/menunggu di StayPoint
            else if (isWaiting)
            {
                waitTimer -= statusUpdateInterval;
                if (agent != null && agent.enabled)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }

                if (waitTimer <= 0f)
                {
                    isWaiting = false;
                    AdvanceToNextWaypoint();
                }
            }

            yield return statusUpdateDelay;
        }
    }

    /// <summary>
    /// Loop rekursif berkala untuk memproses kecerdasan AI navigasi dan deteksi stuck (hemat CPU).
    /// </summary>
    private System.Collections.IEnumerator AILoop()
    {
        if (isKO) yield break;

        // KODENYA TERSPESIALISASI: Jika game masih loading, tunggu hingga loading selesai sebelum memproses AI
        if (ObjectiveManager.IsLoading)
        {
            yield return aiUpdateDelay;
            StartCoroutine(AILoop());
            yield break;
        }

        if (!isStunned && !isWaiting && waypointsList.Count > 0)
        {
            Transform targetWaypoint = waypointsList[currentWaypointIndex];

            // Cek apakah sudah sampai di waypoint target
            bool arrived = false;
            if (agent != null && agent.enabled)
            {
                if (!agent.pathPending && agent.remainingDistance <= arrivalDistanceThreshold)
                {
                    arrived = true;
                }
            }
            else
            {
                float sqrDistanceToWaypoint = (transform.position - targetWaypoint.position).sqrMagnitude;
                if (sqrDistanceToWaypoint <= sqrArrivalDistanceThreshold)
                {
                    arrived = true;
                }
            }

            if (arrived)
            {
                if (IsStayPoint(targetWaypoint))
                {
                    isWaiting = true;
                    float currentStayDuration = useDefaultPresets ? Random.Range(activeMinStay, activeMaxStay) : stayDuration;
                    waitTimer = currentStayDuration;
                    if (agent != null && agent.enabled)
                    {
                        agent.isStopped = true;
                        agent.velocity = Vector3.zero;
                    }
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
                // Deteksi Stuck (kalkulasi jarak tempuh sejak interval pembaruan terakhir)
                if (agent != null && agent.enabled)
                {
                    float sqrDistanceMoved = (transform.position - lastPosition).sqrMagnitude;
                    if (sqrDistanceMoved < 0.0025f && agent.velocity.sqrMagnitude < 0.01f) // Bergerak kurang dari 5 cm dalam interval update
                    {
                        stuckTimer += aiUpdateInterval;
                        if (stuckTimer >= stuckTimeout)
                        {
                            stuckTimer = 0f;
                            AdvanceToNextWaypoint();
                            yield return aiUpdateDelay;
                            StartCoroutine(AILoop());
                            yield break;
                        }
                    }
                    else
                    {
                        stuckTimer = 0f;
                    }
                    lastPosition = transform.position;

                    // Pastikan agent sedang memiliki rute aktif menuju target waypoint
                    if (!agent.hasPath || agent.isStopped)
                    {
                        agent.SetDestination(targetWaypoint.position);
                        agent.isStopped = false;
                    }
                }
            }
        }

        yield return aiUpdateDelay;
        StartCoroutine(AILoop());
    }

    /// <summary>
    /// Menginisialisasi rute patroli dengan memasukkan waypoint dari array StayPoints dan NormalWaypoints.
    /// </summary>
    private void InitializeWaypoints()
    {
        waypointsList.Clear();
        stayPointsSet.Clear();

        if (stayPoints != null)
        {
            waypointsList.AddRange(stayPoints);

            for (int i = 0; i < stayPoints.Length; i++)
            {
                if (stayPoints[i] != null)
                {
                    stayPointsSet.Add(stayPoints[i]);
                }
            }
        }

        if (normalWaypoints != null)
        {
            waypointsList.AddRange(normalWaypoints);
        }

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

        // LOGIC DI BALIK LAYAR: Acak kecepatan untuk jalur berikutnya agar pergerakan terasa dinamis
        if (useDefaultPresets)
        {
            maxSpeed = Random.Range(activeMinSpeed, activeMaxSpeed);
            if (agent != null)
            {
                agent.speed = maxSpeed;
            }
        }

        if (agent != null && agent.enabled)
        {
            agent.SetDestination(nextWaypoint.position);
            agent.isStopped = false;
        }
    }

    /// <summary>
    /// Konfigurasi default sifat/lore NPC secara otomatis saat inisialisasi awal.
    /// </summary>
    private void ApplyLorePreset()
    {
        if (lorePreset != null)
        {
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
            switch (npcType)
            {
                case NPCType.YoungMan:
                    activeMinSpeed = 8.0f;
                    activeMaxSpeed = 9.0f;
                    activeMinStay = 5.0f;
                    activeMaxStay = 6.0f;
                    acceleration = 10f;
                    deceleration = 12f;
                    turnSensitivity = 2.0f;
                    break;

                case NPCType.YoungWoman:
                    activeMinSpeed = 5.0f;
                    activeMaxSpeed = 6.0f;
                    activeMinStay = 5.0f;
                    activeMaxStay = 6.0f;
                    acceleration = 6f;
                    deceleration = 8f;
                    turnSensitivity = 1.5f;
                    break;

                case NPCType.Mother:
                    activeMinSpeed = 9.0f;
                    activeMaxSpeed = 10.0f;
                    activeMinStay = 3.0f;
                    activeMaxStay = 4.0f;
                    acceleration = 16f;
                    deceleration = 20f;
                    turnSensitivity = 2.5f;
                    break;

                case NPCType.Childish:
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

        maxSpeed = Random.Range(activeMinSpeed, activeMaxSpeed);

        if (agent != null)
        {
            agent.speed = maxSpeed;
            agent.acceleration = acceleration;
            agent.angularSpeed = turnSensitivity * 100f;
            agent.stoppingDistance = arrivalDistanceThreshold;
        }
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

            if (weaponRb != null && weaponRb.velocity.magnitude > weaponVelocityThreshold)
            {
                float weight = objScript.ObjWeight;
                
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

        if (trolley != null)
        {
            NPCController otherNpc = collision.gameObject.GetComponent<NPCController>();
            if (otherNpc == null) otherNpc = collision.gameObject.GetComponentInParent<NPCController>();
            if (otherNpc == null) otherNpc = collision.gameObject.GetComponentInChildren<NPCController>();

            if (otherNpc == null)
            {
                float playerSpeed = trolley.CurrentSpeed;
                if (playerSpeed > playerCollisionSpeedThreshold)
                {
                    TriggerKO();
                }
            }
        }
    }

    /// <summary>
    /// Memicu status Stun sementara pada NPC.
    /// </summary>
    private void TriggerStun(float duration, Collision collision, float weight)
    {
        isStunned = true;
        stunTimer = duration;
        isWaiting = false;

#if UNITY_EDITOR
        Debug.Log($"[NPCController] NPC '{gameObject.name}' Ter-Stun {duration} detik! (Berat Senjata: {weight} kg)");
#endif

        if (rb != null)
        {
            rb.isKinematic = false;
            Vector3 pushDirection = (transform.position - collision.transform.position).normalized;
            pushDirection.y = stunUpwardFactor;
            rb.AddForce(pushDirection.normalized * stunPushbackForce, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// Memicu status Knockout (KO) pada NPC.
    /// </summary>
    private void TriggerKO()
    {
        isKO = true;
        isStunned = false;

        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.RegisterNpcKO();
        }

        isWaiting = false;
        
        if (agent != null)
        {
            agent.enabled = false;
        }

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.None;
            
            Vector3 bounceDirection = Vector3.up * koUpwardForce + Random.onUnitSphere * koRandomForceRange;
            rb.AddForce(bounceDirection, ForceMode.Impulse);
            rb.AddTorque(Random.onUnitSphere * koTorqueForce, ForceMode.Impulse);
        }
    }
}
