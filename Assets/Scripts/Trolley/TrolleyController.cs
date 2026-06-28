using UnityEngine;

/// <summary>
/// Script ini berfungsi untuk mengontrol pergerakan fisik Trolley (dan Player yang mendorongnya).
/// Menggunakan Rigidbody untuk memberikan efek pergerakan yang "berat" dan realistis (Need for Seat style).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class TrolleyController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Referensi ke script FloatingJoystick untuk mengambil data input.")]
    [SerializeField] private FloatingJoystick joystick;

    [Header("Movement Settings")]
    [Tooltip("Kecepatan maksimum trolley saat berjalan lurus.")]
    [SerializeField] private float maxSpeed = 8f;

    [Tooltip("Seberapa cepat trolley berakselerasi menuju kecepatan maksimum.")]
    [SerializeField] private float acceleration = 5f;

    [Tooltip("Seberapa cepat trolley mengerem saat joystick dilepas.")]
    [SerializeField] private float deceleration = 8f;

    [Tooltip("Batas mati input joystick (deadzone). Input di bawah nilai ini akan diabaikan.")]
    [SerializeField] private float inputDeadzone = 0.1f;

    [Header("Steering Settings")]
    // [Tooltip("Kecepatan rotasi/belok dasar saat trolley bergerak lambat.")]
    // [SerializeField] private float baseTurnSpeed = 90f; // Dinonaktifkan sementara karena rotasi menggunakan akumulasi swipe langsung

    [Tooltip("Ambang batas rasio kecepatan (0-1) di mana kemudi mulai terkunci sangat berat (full speed threshold).")]
    [SerializeField] private float heavyTurnSpeedThreshold = 0.75f;

    [Tooltip("Pengali kecepatan belok saat berada di kecepatan maksimum. Semakin kecil nilainya, semakin sukar dibelokkan saat kencang (Inersia berat).")]
    [Range(0.05f, 0.8f)]
    [SerializeField] private float turnDifficultyAtMaxSpeed = 0.45f;

    [Tooltip("Sudut belok minimal untuk memicu rotasi fisik Rigidbody.")]
    [SerializeField] private float minTurnAngleThreshold = 0.0001f;

    [Header("Weight/Cargo Settings")]
    [Tooltip("Berat barang bawaan saat ini (bisa dimodifikasi oleh script lain nanti).")]
    public float currentWeight = 0f;

    [Tooltip("Seberapa besar pengaruh berat barang terhadap penurunan akselerasi dan belokan.")]
    [SerializeField] private float weightImpactMultiplier = 0.15f;

    [Header("Upright Stability Settings")]
    [Tooltip("Interval waktu (detik) untuk mereset rotasi X dan Z agar tetap 0. Set ke 0 untuk mereset setiap frame.")]
    [SerializeField] private float rotationResetInterval = 0.5f;

    // Variabel internal
    private Rigidbody rb;
    private float currentForwardSpeed = 0f;
    private float currentSidewaySpeed = 0f;

    // Menampung akumulasi input geser horizontal (yaw) dari layar kanan selama satu frame.
    // Akumulasi dilakukan di Update() pada script kamera dan diterapkan serta di-reset di FixedUpdate() pada script ini.
    // Metode ini mencegah input hilang akibat perbedaan frekuensi pemanggilan antara Update() dan FixedUpdate().
    private float accumulatedSwipeRotationInput = 0f;

    // ==========================================
    // Properties untuk dibaca oleh TouchCameraController
    // ==========================================

    // Mengekspos pengali kesulitan berbelok saat kecepatan maksimum ke script kamera.
    public float TurnDifficultyAtMaxSpeed => turnDifficultyAtMaxSpeed;

    // Menghitung faktor penurunan kecepatan/kemudahan putar berdasarkan berat barang bawaan saat ini.
    // Rumus: 1 / (1 + (berat * pengali_pengaruh)).
    // LOGIC DI BALIK LAYAR: Semakin besar barang bawaan, penyebut semakin besar sehingga weightFactor semakin kecil (mendekati 0).
    // Hal ini digunakan untuk mengurangi akselerasi dan daya belok secara proporsional.
    public float WeightFactor => 1f / (1f + (currentWeight * weightImpactMultiplier));

    // Mengekspos kecepatan maksimum dasar trolley untuk kalkulasi rasio tabrakan eksternal.
    public float MaxSpeed => maxSpeed;

    // Menghitung rasio kecepatan saat ini terhadap kecepatan maksimum acuan (maxSpeed yang disesuaikan dengan berat).
    // LOGIC DI BALIK LAYAR: Menggunakan kecepatan dari variabel persistent (Forward & Sideway) dibanding dengan maxSpeed * WeightFactor.
    // Dibatasi antara 0 dan 1 (Mathf.Clamp01). Digunakan oleh UI, kontrol kemudi, dan detektor kerusakan (TrolleyCollisionHandler).
    public float CurrentSpeedRatio
    {
        get
        {
            float speedSqr = currentSidewaySpeed * currentSidewaySpeed + currentForwardSpeed * currentForwardSpeed;
            float targetMaxSpeed = maxSpeed * WeightFactor;
            if (targetMaxSpeed < 0.001f) return 0f;
            return Mathf.Clamp01(Mathf.Sqrt(speedSqr) / targetMaxSpeed);
        }
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>();

        // Mengatur parameter Rigidbody agar sesuai dengan simulasi fisik trolley
        rb.useGravity = true;
        rb.drag = 0.1f; // Sedikit hambatan udara agar tidak seluncur tanpa henti
        rb.angularDrag = 2f; // Hambatan rotasi tinggi agar tidak berputar berlebihan (stabil)

        // PENTING: Kunci rotasi X dan Z agar trolley tidak terguling/miring saat menabrak dinding
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        // Auto-assign joystick jika kosong di Inspector demi menghindari NullReferenceException
        if (joystick == null)
        {
            joystick = FindObjectOfType<FloatingJoystick>();
        }

        // Jalankan Coroutine mandiri untuk menjaga agar troli tetap tegak lurus (X/Z = 0)
        StartCoroutine(KeepUprightCoroutine());
    }

    private void FixedUpdate()
    {
        if (joystick == null) return;

        // Ambil input arah dari joystick (X untuk kiri-kanan, Y untuk maju-mundur)
        Vector2 input = joystick.Direction;

        MoveTrolley(input);
        RotateTrolley();
    }

    /// <summary>
    /// Mengontrol akselerasi maju/mundur/kiri/kanan trolley relatif terhadap arah hadap saat ini.
    /// Membagi area input joystick menjadi 4 sektor (Maju, Kanan, Mundur, Kiri) untuk mendeteksi
    /// perubahan arah dan menyesuaikan tingkat pengereman (deceleration) agar kontrol terasa responsif.
    /// </summary>
    private void MoveTrolley(Vector2 input)
    {
        // Ambil faktor beban barang untuk membatasi performa akselerasi & deselerasi trolley
        float weightFactor = WeightFactor;

        // 1. Tentukan target kecepatan lokal berdasarkan input joystick (X untuk menyamping, Y untuk maju/mundur)
        float targetForwardSpeed = input.y * maxSpeed;
        float targetSidewaySpeed = input.x * maxSpeed;

        float activeRate = acceleration;

        if (input.sqrMagnitude > (inputDeadzone * inputDeadzone))
        {
            float currentVelSqr = currentSidewaySpeed * currentSidewaySpeed + currentForwardSpeed * currentForwardSpeed;
            if (currentVelSqr > 0.01f)
            {
                // Hitung sudut input joystick (0 di sumbu Y/maju, searah jarum jam)
                float inputAngle = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg;
                if (inputAngle < 0) inputAngle += 360f;

                int inputSector = 0;
                if (inputAngle >= 315f || inputAngle < 45f) inputSector = 0; // Maju (Up)
                else if (inputAngle >= 45f && inputAngle < 135f) inputSector = 1; // Kanan (Right)
                else if (inputAngle >= 135f && inputAngle < 225f) inputSector = 2; // Mundur (Down)
                else if (inputAngle >= 225f && inputAngle < 315f) inputSector = 3; // Kiri (Left)

                // Hitung sudut arah pergerakan fisik saat ini
                float moveAngle = Mathf.Atan2(currentSidewaySpeed, currentForwardSpeed) * Mathf.Rad2Deg;
                if (moveAngle < 0) moveAngle += 360f;

                int moveSector = 0;
                if (moveAngle >= 315f || moveAngle < 45f) moveSector = 0; // Maju (Up)
                else if (moveAngle >= 45f && moveAngle < 135f) moveSector = 1; // Kanan (Right)
                else if (moveAngle >= 135f && moveAngle < 225f) moveSector = 2; // Mundur (Down)
                else if (moveAngle >= 225f && moveAngle < 315f) moveSector = 3; // Kiri (Left)

                // Hitung selisih sektor secara melingkar (0, 1, 2, atau 3)
                int diff = Mathf.Abs(inputSector - moveSector);
                if (diff > 2) diff = 4 - diff;

                if (diff == 2)
                {
                    // Transisi berlawanan arah (180 derajat): gunakan rem penuh (deceleration)
                    activeRate = deceleration;
                }
                else if (diff == 1)
                {
                    // Transisi berbelok tegak lurus (90 derajat): gunakan rem setengah (deceleration * 0.5f)
                    activeRate = deceleration * 0.5f;
                }
                else
                {
                    // Arah yang sama (0 derajat): gunakan akselerasi normal
                    activeRate = acceleration;
                }
            }

            // Terapkan penambahan kecepatan menuju target dengan tingkat akselerasi/pengereman dinamis
            currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, targetForwardSpeed, activeRate * weightFactor * Time.fixedDeltaTime);
            currentSidewaySpeed = Mathf.MoveTowards(currentSidewaySpeed, targetSidewaySpeed, activeRate * weightFactor * Time.fixedDeltaTime);
        }
        else
        {
            // Tidak ada input joystick: rem normal menuju diam (0)
            currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, 0f, deceleration * weightFactor * Time.fixedDeltaTime);
            currentSidewaySpeed = Mathf.MoveTowards(currentSidewaySpeed, 0f, deceleration * weightFactor * Time.fixedDeltaTime);
        }

        // 4. Hitung vektor pergerakan lokal berdasarkan kecepatan persistent (transform.forward dan transform.right).
        //    Ini mengubah kecepatan lokal menjadi orientasi arah dunia (World Space) yang tepat sesuai dengan arah hadap trolley.
        Vector3 localVelocity = (transform.forward * currentForwardSpeed) + (transform.right * currentSidewaySpeed);

        // 5. Terapkan kecepatan ke Rigidbody dengan mempertahankan gravitasi pada sumbu Y (rb.velocity.y).
        rb.velocity = new Vector3(localVelocity.x, rb.velocity.y, localVelocity.z);
    }

    /// <summary>
    /// Menambahkan input rotasi horizontal dari swipe layar kanan (dipanggil oleh TouchCameraController di Update).
    /// </summary>
    public void AddSwipeRotationInput(float amount)
    {
        // Menjumlahkan delta sentuhan horizontal setiap frame rendering (Update) ke penampung sementara.
        // Ini memastikan gerakan jari sekecil apa pun diakumulasikan dengan akurat sebelum dieksekusi di FixedUpdate.
        accumulatedSwipeRotationInput += amount;
    }

    /// <summary>
    /// Mengontrol rotasi/belokan trolley berdasarkan input swipe dengan efek inersia (semakin kencang/berat semakin sulit belok).
    /// </summary>
    private void RotateTrolley()
    {
        // 1. Dapatkan rasio kecepatan saat ini (mengacu pada virtual max speed yang disesuaikan berat).
        float speedRatio = CurrentSpeedRatio;

        // 2. Tentukan pengali kemudahan putar berdasarkan kecepatan (Need for Seat style).
        // LOGIC DI BALIK LAYAR:
        // Jika kecepatan mencapai atau melebihi batas (heavyTurnSpeedThreshold), 
        // kita paksa pengali rotasi menjadi turnDifficultyAtMaxSpeed (belokan sangat berat/kaku).
        // Jika di bawah threshold, kita lakukan interpolasi linier (Lerp) secara mulus dari ringan (1.0f) ke berat.
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

        // 3. Ambil faktor beban barang. Semakin banyak barang, trolley semakin sukar dirubah orientasinya (berat di semua kondisi).
        float weightFactor = WeightFactor;

        // 4. Hitung sudut putar akhir (dalam derajat) untuk frame fisika ini.
        //    Sudut dihitung dari akumulasi geser layar dikali faktor reduksi kecepatan dan berat beban barang.
        //    Ini menjamin belokan terasa berat baik saat diam, berjalan lambat, maupun saat meluncur kencang.
        float turnAngle = accumulatedSwipeRotationInput * speedTurnMultiplier * weightFactor;

        // 5. Reset akumulator setelah nilainya digunakan agar tidak terjadi double-rotation pada frame berikutnya.
        accumulatedSwipeRotationInput = 0f;

        // 6. Jika terdapat perubahan sudut rotasi yang cukup signifikan
        if (Mathf.Abs(turnAngle) > minTurnAngleThreshold)
        {
            // Buat rotasi delta berupa Quaternion mengelilingi sumbu Y (horizontal).
            Quaternion turnRotation = Quaternion.Euler(0f, turnAngle, 0f);
            
            // LOGIC DI BALIK LAYAR:
            // Menggunakan rb.MoveRotation() untuk memutar Rigidbody secara fisik.
            // Rumus: rb.rotation * turnRotation mengalikan orientasi saat ini dengan delta rotasi untuk mendapatkan orientasi baru.
            // Mengapa rb.MoveRotation? Karena ini adalah metode bawaan Unity yang paling aman untuk objek ber-Rigidbody.
            // Metode ini memungkinkan mesin fisika menghitung interaksi tabrakan (collision) dengan baik sepanjang lintasan beloknya,
            // berbeda jika kita langsung memanipulasi transform.rotation secara mentah (teleportasi rotasi yang bisa menembus dinding).
            rb.MoveRotation(rb.rotation * turnRotation);
        }
    }

    /// <summary>
    /// Coroutine mandiri untuk menjaga agar troli tetap tegak lurus (rotation X & Z = 0).
    /// Mengurangi beban pemrosesan di FixedUpdate/Update.
    /// </summary>
    private System.Collections.IEnumerator KeepUprightCoroutine()
    {
        // Cache yield instruction untuk mencegah alokasi GC (Garbage Collection) di Mobile WebGL
        WaitForSeconds delay = rotationResetInterval > 0f ? new WaitForSeconds(rotationResetInterval) : null;

        while (true)
        {
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
