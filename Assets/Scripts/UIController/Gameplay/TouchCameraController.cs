using UnityEngine;

/// <summary>
/// Script ini berfungsi untuk merotasikan kamera (orbit) ketika pemain mengusap/menggeser layar bagian kanan.
/// Kompatibel dengan input sentuhan layar HP (Touch) maupun klik seret mouse di PC/Editor Unity.
/// </summary>
public class TouchCameraController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Objek yang akan diputar oleh kamera (biasanya Camera Target yang berada di dalam objek Player).")]
    [SerializeField] private Transform targetToRotate;

    [Tooltip("Referensi ke TrolleyController untuk memutar badan trolley secara horizontal.")]
    [SerializeField] private TrolleyController trolley;

    [Header("Rotation Settings")]
    [Tooltip("Sensitivitas rotasi horizontal (kiri-kanan).")]
    [SerializeField] private float sensitivityX = 0.2f;

    [Tooltip("Sensitivitas rotasi vertikal (atas-bawah).")]
    [SerializeField] private float sensitivityY = 0.2f;

    // [Tooltip("Batas sudut minimum kemiringan kamera ke bawah (derajat).")]
    // [SerializeField] private float minPitch = -20f; // Dinonaktifkan selama playtesting (kamera vertikal stagnan)

    // [Tooltip("Batas sudut maksimum kemiringan kamera ke atas (derajat).")]
    // [SerializeField] private float maxPitch = 50f; // Dinonaktifkan selama playtesting (kamera vertikal stagnan)

    [Header("Inversion Settings")]
    [Tooltip("Balikkan arah rotasi horizontal (kiri-kanan). Berguna jika arah swipe terasa terbalik di mobile WebGL.")]
    [SerializeField] private bool invertHorizontal = true;

    // Nilai rotasi internal dalam Euler Angle
    private float rotationX = 0f; // Rotasi horizontal (mengelilingi sumbu Y) - Hanya digunakan jika trolley tidak ditemukan
    private float rotationY = 0f; // Rotasi vertikal (mengelilingi sumbu X)

    // Menyimpan ID jari sentuhan yang sedang aktif mengontrol kamera (untuk mobile)
    private int activeTouchId = -1;

    private void Start()
    {
        if (targetToRotate == null)
        {
            // Jika lupa di-assign, gunakan transform tempat script ini ditempelkan
            targetToRotate = transform;
        }

        if (trolley == null)
        {
            // Cari TrolleyController secara otomatis di scene jika belum di-assign
            trolley = FindObjectOfType<TrolleyController>();
        }

        // Ambil rotasi awal target agar transisi kamera tidak melompat saat pertama kali digeser
        Vector3 currentRotation = targetToRotate.eulerAngles;
        rotationY = currentRotation.x;
        
        // Normalisasi rotationY agar tidak terjadi snap jika kemiringan awal negatif (e.g. 340 derajat menjadi -20)
        if (rotationY > 180f) rotationY -= 360f;

        rotationX = currentRotation.y;
    }

    private void Update()
    {
        // KODENYA TERSPESIALISASI: Blokir seluruh input kamera jika game masih loading
        if (ObjectiveManager.IsLoading)
        {
            activeTouchId = -1;
            return;
        }

        // LOGIC DI BALIK LAYAR:
        // Jika game sedang di-pause atau pemain sudah menang (ditandai dengan Time.timeScale = 0),
        // kita skip seluruh pembacaan input sentuhan dan pemrosesan rotasi kamera.
        // Ini memastikan kamera tetap diam saat menu pause atau layar You Win sedang aktif di layar.
        if (Mathf.Approximately(Time.timeScale, 0f))
        {
            // Reset status sentuhan aktif agar saat game di-resume tidak ada sentuhan yang 'menggantung'
            activeTouchId = -1;
            return;
        }

        // Deteksi input sentuhan untuk Mobile dan Mouse untuk PC/Editor
        #if UNITY_EDITOR || UNITY_STANDALONE
            HandleMouseInput();
        #else
            HandleTouchInput();
        #endif
    }

    /// <summary>
    /// Logika deteksi geser layar menggunakan Mouse (untuk pengujian di PC/Unity Editor).
    /// </summary>
    private void HandleMouseInput()
    {
        // Saat tombol mouse kiri ditekan pertama kali
        if (Input.GetMouseButtonDown(0))
        {
            // Pastikan posisi klik pertama berada di area sebelah kanan layar (bukan di area joystick)
            if (Input.mousePosition.x > Screen.width / 2f)
            {
                activeTouchId = 0; // Tandai bahwa klik kanan aktif
            }
        }

        // Saat tombol mouse kiri dilepas
        if (Input.GetMouseButtonUp(0))
        {
            activeTouchId = -1; // Reset status klik
        }

        // Jika klik mouse terdaftar di kanan dan sedang diseret (drag)
        if (activeTouchId == 0 && Input.GetMouseButton(0))
        {
            float mouseX = Input.GetAxis("Mouse X") * sensitivityX * 50f;
            float mouseY = Input.GetAxis("Mouse Y") * sensitivityY * 50f;

            ApplyRotation(mouseX, mouseY);
        }
    }

    /// <summary>
    /// Logika deteksi geser layar menggunakan Jari Sentuh (untuk perangkat Mobile Android/iOS).
    /// </summary>
    private void HandleTouchInput()
    {
        // Iterasi semua sentuhan jari yang terdeteksi di layar secara zero-alloc
        int touchCount = Input.touchCount;
        for (int i = 0; i < touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            
            // 1. Deteksi awal sentuhan (TouchPhase.Began)
            if (touch.phase == TouchPhase.Began)
            {
                // Pastikan jari menyentuh sisi sebelah kanan layar
                if (touch.position.x > Screen.width / 2f && activeTouchId == -1)
                {
                    // Simpan ID jari ini agar hanya jari ini yang mengontrol kamera (mencegah bentrokan dengan joystick)
                    activeTouchId = touch.fingerId;
                }
            }

            // 2. Jika jari yang tepat sedang digeser (TouchPhase.Moved)
            if (touch.fingerId == activeTouchId && touch.phase == TouchPhase.Moved)
            {
                // Ambil delta pergeseran piksel jari, lalu kalikan dengan sensitivitas
                float deltaX = touch.deltaPosition.x * sensitivityX;
                float deltaY = touch.deltaPosition.y * sensitivityY;

                ApplyRotation(deltaX, deltaY);
            }

            // 3. Deteksi sentuhan selesai (jari diangkat atau dibatalkan)
            if (touch.fingerId == activeTouchId && (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled))
            {
                activeTouchId = -1; // Reset agar layar kanan bisa menerima sentuhan baru
            }
        }
    }

    /// <summary>
    /// Logika penerapan rotasi ke objek Target dan Trolley.
    /// </summary>
    private void ApplyRotation(float deltaX, float deltaY)
    {
        // 1. Menggeser jari secara VERTIKAL (Y) - DINONAKTIFKAN UNTUK PLAYTESTING
        //    Kamera atas-bawah dibikin stagnan (tidak berubah). Nilai awal tetap dipertahankan.
        //    Kode di bawah ini di-comment agar tidak merubah rotationY selama pengetesan.
        /*
        rotationY -= deltaY;
        rotationY = Mathf.Clamp(rotationY, minPitch, maxPitch);
        */

        // 2. Menggeser jari secara HORIZONTAL (X) akan merotasikan orientasi horizontal (Yaw).
        //    LOGIC DI BALIK LAYAR: Jika invertHorizontal diset true, kita balikkan tanda deltaX agar
        //    arah rotasi sesuai dengan harapan pemain (usap kiri berputar ke kiri, usap kanan ke kanan).
        float finalDeltaX = invertHorizontal ? -deltaX : deltaX;

        if (trolley != null)
        {
            // Kirim input rotasi ter-invert ke TrolleyController untuk diproses secara fisik di FixedUpdate()
            trolley.AddSwipeRotationInput(finalDeltaX);
        }
        else
        {
            // Fallback jika trolley tidak ada di scene
            rotationX += finalDeltaX;
        }
    }

    /// <summary>
    /// LateUpdate dipanggil oleh Unity setiap frame setelah semua fungsi Update dan perhitungan fisika (FixedUpdate) selesai dijalankan.
    /// </summary>
    private void LateUpdate()
    {
        if (targetToRotate == null) return;

        if (trolley != null)
        {
            // LOGIC DI BALIK LAYAR (Sinkronisasi Orbit Kamera dan Hadapan Trolley):
            // Mengapa di LateUpdate?
            // Jika kita menyelaraskan posisi/rotasi kamera di Update(), kamera akan bergetar (jitter) karena posisi/rotasi
            // trolley sedang dimutasi di FixedUpdate() oleh sistem fisika Unity. LateUpdate menjamin posisi & rotasi trolley 
            // sudah "final" untuk frame tersebut sebelum kamera diposisikan.
            // 
            // Bagaimana cara kerjanya?
            // - Sumbu X (Pitch / Mendongak): Kita mengambil nilai 'rotationY' (yang dikontrol oleh geser layar vertikal).
            // - Sumbu Y (Yaw / Berputar Kiri-Kanan): Kita secara dinamis mengambil sudut rotasi horizontal aktual dari trolley saat ini ('trolley.transform.eulerAngles.y').
            //   Hal ini memastikan kamera akan SELALU terpusat di belakang trolley secara mulus, baik saat trolley berputar karena digeser layar, 
            //   karena belok otomatis, maupun karena terpental akibat menabrak objek di dunia game.
            // - Sumbu Z (Roll / Miring): Kita set 0f agar horizon kamera selalu tegak lurus dan stabil.
            targetToRotate.rotation = Quaternion.Euler(rotationY, trolley.transform.eulerAngles.y, 0f);
        }
        else
        {
            // Fallback: Gunakan nilai rotasi internal biasa jika trolley tidak terpasang.
            targetToRotate.rotation = Quaternion.Euler(rotationY, rotationX, 0f);
        }
    }
}
