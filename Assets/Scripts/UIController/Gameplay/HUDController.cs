using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Script ini berfungsi sebagai pengendali UI HUD (Heads-Up Display) khususnya untuk tombol "Grab" dan "Throw".
/// Bertanggung jawab atas pengelolaan transisi fisik perpindahan barang (Goods) ke Trolley serta persenjataan (Weapon) ke Player,
/// penanganan penumpukan senjata (maksimal 1 senjata), dan kalkulasi gaya lemparan fisika.
/// </summary>
public class HUDController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Referensi ke controller interaksi untuk mendapatkan objek terdekat yang sedang di-highlight.")]
    [SerializeField] private TrolleyInteractController interactController;

    [Tooltip("Titik penempatan barang masuk di dalam keranjang Trolley (ObjSpawnPoint).")]
    [SerializeField] private Transform objSpawnPoint;

    [Tooltip("Titik penempatan senjata di tangan Player (WeaponSpawnPoint).")]
    [SerializeField] private Transform weaponSpawnPoint;

    [Tooltip("Transform Player untuk acuan arah hadap pelemparan senjata.")]
    [SerializeField] private Transform playerTransform;

    [Tooltip("Referensi ke TrolleyAreaDetector untuk mengambil/mengeluarkan barang saat di-equip.")]
    [SerializeField] private TrolleyAreaDetector trolleyAreaDetector;

    [Header("Throw & Aim Settings")]
    [Tooltip("Tombol Throw di UI untuk diaktifkan/dinonaktifkan secara dinamis.")]
    [SerializeField] private Button throwButton;

    [Tooltip("Image UI Crosshair untuk efek bidikan (fade in/out).")]
    [SerializeField] private Image crosshairImage;

    [Header("Settings")]
    [Tooltip("Kecepatan gerak linear objek saat melayang menuju titik spawn.")]
    [SerializeField] private float grabMoveSpeed = 8f;

    [Tooltip("Besar gaya dorong impuls saat melempar senjata.")]
    [SerializeField] private float throwForce = 25f;

    [Tooltip("Tinggi offset vertikal (Y) ke atas tempat senjata diposisikan sebelum dilempar agar bersih dari tabrakan trolley.")]
    [SerializeField] private float throwUpwardOffset = 1.2f;

    [Header("Menu & Pause UI References")]
    [Tooltip("Panel UI kemenangan (BGYouWin)")]
    [SerializeField] private GameObject bgYouWinPanel;

    [Tooltip("Panel UI Pause (BGPause)")]
    [SerializeField] private GameObject bgPausePanel;

    [Tooltip("Nama scene Main Menu untuk di-load ketika quit.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    // Menyimpan referensi senjata yang sedang dipegang aktif oleh player.
    // Jika player mengambil senjata baru, referensi ini digunakan untuk menjatuhkan senjata lama terlebih dahulu.
    private GameObject equippedWeapon = null;

    // Cache kamera utama untuk optimalisasi WebGL Mobile (menghindari Camera.main)
    [SerializeField] private Camera mainCamera;

    private void Start()
    {
        // LOGIC DI BALIK LAYAR:
        // Setiap kali game di-start (atau di-load ulang), pastikan Time.timeScale bernilai 1.0f 
        // agar game berjalan normal (tidak dalam kondisi ter-pause).
        Time.timeScale = 1f;

        // Nonaktifkan panel pause dan panel you win di awal agar bersih
        if (bgPausePanel != null)
        {
            bgPausePanel.SetActive(false);
        }

        if (bgYouWinPanel != null)
        {
            bgYouWinPanel.SetActive(false);
        }

        // Pastikan crosshair dalam kondisi mati (alpha = 0) di awal game
        if (crosshairImage != null)
        {
            Color c = crosshairImage.color;
            c.a = 0f;
            crosshairImage.color = c;
        }

        // Auto-assign TrolleyAreaDetector jika kosong
        if (trolleyAreaDetector == null)
        {
            trolleyAreaDetector = FindObjectOfType<TrolleyAreaDetector>();
        }

        // Perbarui status keaktifan tombol throw di awal
        UpdateThrowButtonState();
    }



    /// <summary>
    /// Mengambil objek spesifik yang dipilih oleh pemain dari daftar UI barang di sekitar (PUBG style).
    /// Mengirimkan seluruh tipe objek (Goods maupun Weapon) ke dalam trolley.
    /// </summary>
    public void GrabObject(ObjectScript targetObjScript)
    {
        // KODENYA TERSPESIALISASI: Blokir pengambilan objek jika game masih loading
        if (ObjectiveManager.IsLoading) return;

        if (targetObjScript == null) return;

        GameObject targetObj = targetObjScript.gameObject;

        // Cari Outline untuk dihapus dari daftar kandidat agar UI ter-update
        Outline targetOutline = targetObjScript.GetComponent<Outline>();
        if (targetOutline == null) targetOutline = targetObjScript.GetComponentInChildren<Outline>();
        if (targetOutline == null) targetOutline = targetObjScript.GetComponentInParent<Outline>();

        if (targetOutline != null && interactController != null)
        {
            interactController.RemoveCandidate(targetOutline);
        }

        if (objSpawnPoint == null)
        {
            Debug.LogError("[HUDController] objSpawnPoint belum dihubungkan di Inspector!");
            return;
        }

        // Pindahkan barang ke keranjang trolley secara universal
        StartCoroutine(MoveToTargetCoroutine(targetObj, objSpawnPoint, true));
    }

    /// <summary>
    /// Memindahkan barang dari dalam trolley ke tangan player (siap untuk dilempar).
    /// </summary>
    public void EquipItemFromInventory(ObjectScript targetObjScript)
    {
        // KODENYA TERSPESIALISASI: Blokir equip barang jika game masih loading
        if (ObjectiveManager.IsLoading) return;

        if (targetObjScript == null) return;

        // 1. Jika player sudah memegang sesuatu di tangannya
        if (equippedWeapon != null)
        {
            // Taruh kembali barang lama ke dalam trolley agar tidak hilang secara fisik
            ObjectScript oldScript = equippedWeapon.GetComponent<ObjectScript>();
            if (oldScript == null) oldScript = equippedWeapon.GetComponentInParent<ObjectScript>();
            if (oldScript == null) oldScript = equippedWeapon.GetComponentInChildren<ObjectScript>();

            if (oldScript != null)
            {
                GrabObject(oldScript);
            }
            else
            {
                DropCurrentWeapon();
            }
        }
        else if (weaponSpawnPoint.childCount > 0)
        {
            // Bersihkan objek anak asing yang menempel secara fisik di spawn point senjata
            foreach (Transform child in weaponSpawnPoint)
            {
                ObjectScript oldScript = child.GetComponent<ObjectScript>();
                if (oldScript != null) GrabObject(oldScript);
                else DropWeaponPhysics(child.gameObject);
            }
        }

        // 2. Keluarkan barang dari TrolleyAreaDetector secara logis
        if (trolleyAreaDetector != null)
        {
            trolleyAreaDetector.RemoveItemFromTrolley(targetObjScript);
        }

        // 3. Daftarkan senjata baru ini ke slot genggaman player
        equippedWeapon = targetObjScript.gameObject;

        // Set status menjadi Taken agar sistem tidak menganggap barang ini ada di ground/trolley
        targetObjScript.Status = ObjectStatus.Taken;

        // 4. Tarik senjata ke titik genggam player (isGoods = false agar collider & rigidbody tetap nonaktif saat menempel di tangan)
        StartCoroutine(MoveToTargetCoroutine(targetObjScript.gameObject, weaponSpawnPoint, false));

        // Perbarui status tombol throw
        UpdateThrowButtonState();
    }

    /// <summary>
    /// Mengaktifkan atau menonaktifkan tombol throw di UI tergantung status equipped weapon.
    /// </summary>
    public void UpdateThrowButtonState()
    {
        if (throwButton != null)
        {
            throwButton.interactable = (equippedWeapon != null);
        }

        // KODENYA TERSPESIALISASI: Tampilkan crosshair secara otomatis jika player sedang memegang senjata/barang siap lempar
        FadeCrosshair(equippedWeapon != null);
    }

    private Coroutine crosshairFadeCoroutine;

    /// <summary>
    /// Dipanggil saat tombol Throw mulai ditekan (Pointer Down) untuk memulai bidikan.
    /// Menampilkan UI crosshair secara smooth (fade in).
    /// </summary>
    public void OnThrowButtonPointerDown()
    {
        // KODENYA TERSPESIALISASI: Blokir inisiasi bidikan jika game masih loading
        if (ObjectiveManager.IsLoading) return;

        if (equippedWeapon == null) return;

        // Fade in crosshair
        FadeCrosshair(true);
    }

    /// <summary>
    /// Dipanggil saat tombol Throw dilepaskan (Pointer Up) untuk melakukan lemparan.
    /// </summary>
    public void OnThrowButtonPointerUp()
    {
        // KODENYA TERSPESIALISASI: Blokir lemparan jika game masih loading
        if (ObjectiveManager.IsLoading) return;

        if (equippedWeapon == null) return;

        // Lakukan lemparan fisik menuju crosshair
        ExecuteThrowToCrosshair();

        // Fade out crosshair
        FadeCrosshair(false);
    }

    /// <summary>
    /// Menjalankan transisi fade in/out crosshair secara efisien menggunakan coroutine.
    /// </summary>
    private void FadeCrosshair(bool fadeIn)
    {
        if (crosshairImage == null) return;

        if (crosshairFadeCoroutine != null)
        {
            StopCoroutine(crosshairFadeCoroutine);
        }
        crosshairFadeCoroutine = StartCoroutine(CrosshairFadeCoroutine(fadeIn));
    }

    private IEnumerator CrosshairFadeCoroutine(bool fadeIn)
    {
        float targetAlpha = fadeIn ? 1f : 0f;
        float startAlpha = crosshairImage.color.a;
        float elapsed = 0f;
        float duration = 0.15f; // Kecepatan fade 0.15 detik

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Color c = crosshairImage.color;
            c.a = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            crosshairImage.color = c;
            yield return null;
        }

        Color finalCol = crosshairImage.color;
        finalCol.a = targetAlpha;
        crosshairImage.color = finalCol;
    }

    /// <summary>
    /// Melakukan perhitungan arah lemparan dinamis berbasis crosshair layar dan mengaplikasikan force fisika.
    /// </summary>
    private void ExecuteThrowToCrosshair()
    {
        // Pengecekan fallback jika referensi internal kosong namun ada objek anak secara fisik di WeaponSpawnPoint
        if (equippedWeapon == null && weaponSpawnPoint.childCount > 0)
        {
            equippedWeapon = weaponSpawnPoint.GetChild(0).gameObject;
        }

        if (equippedWeapon != null)
        {
            GameObject weaponToThrow = equippedWeapon;
            equippedWeapon = null; // Kosongkan slot genggaman tangan player

            // 1. Lepaskan parent dari player agar posisinya mandiri di world space
            weaponToThrow.transform.SetParent(null);

            // LOGIC DI BALIK LAYAR (Pencegahan Tabrakan Trolley):
            // Naikkan posisi senjata sedikit ke atas terlebih dahulu agar tidak bertabrakan dengan bodi troli kita sendiri.
            weaponToThrow.transform.position += Vector3.up * throwUpwardOffset;

            // Ubah status menjadi Grounded agar sistem tahu senjata ini dilempar dan bisa disentuh/diambil lagi
            ObjectScript objectScript = weaponToThrow.GetComponent<ObjectScript>();
            if (objectScript == null) objectScript = weaponToThrow.GetComponentInParent<ObjectScript>();
            if (objectScript == null) objectScript = weaponToThrow.GetComponentInChildren<ObjectScript>();
            if (objectScript != null)
            {
                objectScript.Status = ObjectStatus.Grounded;
            }

            // 2. Aktifkan kembali semua collider pada senjata agar dapat menabrak objek lain di scene
            Collider[] colliders = weaponToThrow.GetComponentsInChildren<Collider>();
            foreach (Collider col in colliders)
            {
                col.enabled = true;
            }

            // 3. Aktifkan kembali mesin simulasi Rigidbody fisika
            Rigidbody rb = weaponToThrow.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                // KODENYA TERSPESIALISASI: Arah lemparan dihitung langsung berdasarkan garis pandang crosshair di layar
                // menggunakan ScreenToWorldPoint untuk mendapatkan arah vektor ke dunia 3D yang tepat,
                // sehingga lemparan selalu presisi ke arah crosshair berada (bahkan jika crosshair berpindah posisi).
                Vector2 aimScreenPos = (crosshairImage != null) 
                    ? (Vector2)crosshairImage.transform.position 
                    : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

                Vector3 throwDir;
                if (mainCamera != null)
                {
                    // Ambil titik proyeksi dekat dan titik proyeksi jauh pada sumbu Z pandangan kamera
                    Vector3 nearPoint = mainCamera.ScreenToWorldPoint(new Vector3(aimScreenPos.x, aimScreenPos.y, mainCamera.nearClipPlane + 0.1f));
                    Vector3 farPoint = mainCamera.ScreenToWorldPoint(new Vector3(aimScreenPos.x, aimScreenPos.y, 20f));
                    throwDir = (farPoint - nearPoint).normalized;
                }
                else
                {
                    // Fallback jika kamera utama tidak terpasang
                    throwDir = (playerTransform != null ? playerTransform.forward : Vector3.forward);
                }

                Vector3 finalForce = throwDir * throwForce;

                // Terapkan gaya impuls instan (ForceMode.Impulse)
                rb.AddForce(finalForce, ForceMode.Impulse);
                Debug.Log($"[HUDController] Object '{weaponToThrow.name}' dilempar ke arah crosshair {throwDir} dengan gaya {throwForce}.");
            }

            // Perbarui status tombol throw
            UpdateThrowButtonState();
        }
    }

    /// <summary>
    /// Coroutine untuk menggerakkan objek secara perlahan dengan kecepatan linear menuju target spawn.
    /// </summary>
    private IEnumerator MoveToTargetCoroutine(GameObject obj, Transform target, bool isGoods)
    {
        if (obj == null)
        {
            Debug.LogError("[HUDController] Objek yang akan digerakkan null!");
            yield break;
        }

        if (target == null)
        {
            Debug.LogError("[HUDController] Target tujuan null!");
            yield break;
        }

        Debug.Log($"[HUDController] MoveToTargetCoroutine dimulai untuk '{obj.name}' menuju '{target.name}'. isStatic: {obj.isStatic}");

        // OPTIMALISASI FISIKA WEBGL:
        // Jika objek ditandai sebagai 'Static' di Unity Editor, posisinya tidak akan bisa diubah oleh script.
        // Kita harus mematikan flag isStatic pada objek induk beserta semua anak objeknya sebelum melakukakan pergerakan.
        obj.isStatic = false;
        foreach (Transform child in obj.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.isStatic = false;
        }

        // LOGIC DI BALIK LAYAR (Mengatasi Tabrakan & Fisika Selama Transisi):
        // Jika kita langsung menarik objek yang memiliki Collider & Rigidbody aktif, objek tersebut akan menabrak rak supermarket,
        // lantai, atau bahkan bodi trolley itu sendiri sepanjang jalan. Hal ini mengakibatkan efek getar (jitter) yang merusak visual.
        // Solusi:
        // 1. Matikan seluruh Collider yang ada pada objek (dan anak-anaknya).
        // 2. Jadikan Rigidbody bersifat 'isKinematic = true' agar tidak terpengaruh oleh gaya gravitasi atau tumbukan fisika luar.
        Collider[] colliders = obj.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            col.enabled = false;
        }

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Gerakkan secara perlahan dengan kecepatan linear (Vector3.MoveTowards) hingga sangat dekat dengan target
        while (obj != null && (obj.transform.position - target.position).sqrMagnitude > 0.0025f) // 0.05 * 0.05 = 0.0025f
        {
            // Vector3.MoveTowards menghasilkan pergerakan linear yang stabil (kecepatan konstan)
            obj.transform.position = Vector3.MoveTowards(obj.transform.position, target.position, grabMoveSpeed * Time.deltaTime);
            yield return null;
        }

        // Pastikan objek tepat berada di posisi target
        if (obj != null)
        {
            obj.transform.position = target.position;

            Debug.Log($"[HUDController] '{obj.name}' berhasil sampai di target '{target.name}'. Mengembalikan collider & physics...");

            if (isGoods)
            {
                // KODENYA TERSPESIALISASI:
                // Jangan jadikan child dari objSpawnPoint saat rb/fisika aktif agar tidak membebani pergerakan trolley.
                // Tempatkan di bawah CollectableObjectsParent atau null agar posisinya bebas selama proses jatuh.
                if (ObjectiveManager.Instance != null && ObjectiveManager.Instance.CollectableObjectsParent != null)
                {
                    obj.transform.SetParent(ObjectiveManager.Instance.CollectableObjectsParent);
                }
                else
                {
                    obj.transform.SetParent(null);
                }

                // LOGIC DI BALIK LAYAR (Barang Masuk Trolley):
                // Nyalakan kembali fisika dan collider-nya agar ia jatuh secara alami ke dalam trolley.
                foreach (Collider col in colliders)
                {
                    col.enabled = true;
                }

                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.useGravity = true;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
            else
            {
                // Tempelkan objek menjadi anak (child) dari spawn point agar posisinya mengikuti pergerakan trolley/player (untuk senjata)
                obj.transform.SetParent(target);
                
                // LOGIC DI BALIK LAYAR (Senjata Digenggam):
                // Jika itu senjata (Weapon), biarkan collider & Rigidbody TETAP MATI agar senjata tersebut
                // menempel sempurna di tangan pemain tanpa jatuh ke tanah akibat gravitasi.
                obj.transform.localRotation = Quaternion.identity; // Reset rotasi lokal agar posisi senjata rapi di genggaman
            }
        }
    }

    /// <summary>
    /// Menjatuhkan senjata yang sedang aktif dipegang ke tanah secara fisik.
    /// </summary>
    private void DropCurrentWeapon()
    {
        if (equippedWeapon == null) return;

        DropWeaponPhysics(equippedWeapon);
        equippedWeapon = null;
    }

    /// <summary>
    /// Mengaktifkan kembali collider dan rigidbody pada senjata agar jatuh bebas ke tanah secara fisik.
    /// </summary>
    private void DropWeaponPhysics(GameObject weapon)
    {
        // 1. Putuskan hubungan parent dari player
        weapon.transform.SetParent(null);

        // Ubah status menjadi Grounded agar senjata ini bisa diambil kembali dari tanah
        ObjectScript objectScript = weapon.GetComponent<ObjectScript>();
        if (objectScript == null) objectScript = weapon.GetComponentInParent<ObjectScript>();
        if (objectScript == null) objectScript = weapon.GetComponentInChildren<ObjectScript>();
        if (objectScript != null)
        {
            objectScript.Status = ObjectStatus.Grounded;
        }

        // 2. Aktifkan kembali collider
        Collider[] colliders = weapon.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            col.enabled = true;
        }

        // 3. Aktifkan simulasi fisika Rigidbody dan berikan sedikit dorongan jatuh bebas
        Rigidbody rb = weapon.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // Dorong sedikit ke arah belakang-atas player agar visual jatuhnya terpisah dari badan player
            Vector3 pushDirection = playerTransform != null ? -playerTransform.forward : Vector3.back;
            rb.AddForce(pushDirection * 1.5f + Vector3.up * 1f, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// Membuka panel Pause dan menghentikan jalannya waktu permainan (Time.timeScale = 0).
    /// </summary>
    public void PauseGame()
    {
        // KODENYA TERSPESIALISASI: Blokir pause game jika game masih loading
        if (ObjectiveManager.IsLoading) return;

        if (bgPausePanel != null)
        {
            bgPausePanel.SetActive(true);
        }
        
        // LOGIC DI BALIK LAYAR:
        // Menyetel timeScale ke 0 akan membekukan jalannya fisika dan update berbasis waktu (Time.deltaTime),
        // secara efektif mem-pause gameplay.
        Time.timeScale = 0f;
        Debug.Log("[HUDController] Game Paused.");
    }

    /// <summary>
    /// Diaktifkan ketika tombol "Return" ditekan. Menutup panel pause dan mengembalikan jalannya waktu.
    /// </summary>
    public void ResumeGame()
    {
        if (bgPausePanel != null)
        {
            bgPausePanel.SetActive(false);
        }
        
        // LOGIC DI BALIK LAYAR:
        // Mengembalikan timeScale ke 1.0f agar simulasi fisika dan jalannya permainan kembali normal.
        Time.timeScale = 1f;
        Debug.Log("[HUDController] Game Resumed.");
    }

    /// <summary>
    /// Diaktifkan ketika tombol "Yes" (Play Again) di BGYouWin ditekan. 
    /// Memuat ulang scene aktif saat ini secara bersih.
    /// </summary>
    public void PlayAgain()
    {
        // LOGIC DI BALIK LAYAR:
        // Pastikan timeScale disetel ke 1.0f sebelum berpindah scene, agar game tidak membeku.
        Time.timeScale = 1f;
        
        // Mengambil build index dari scene yang sedang aktif saat ini untuk dimuat ulang.
        int activeSceneIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
        UnityEngine.SceneManagement.SceneManager.LoadScene(activeSceneIndex);
        
        Debug.Log("[HUDController] Re-loading active gameplay scene.");
    }

    /// <summary>
    /// Diaktifkan ketika tombol "No" (BGYouWin) atau "Quit" (BGPause) ditekan.
    /// Memuat scene Main Menu.
    /// </summary>
    public void QuitToMainMenu()
    {
        // LOGIC DI BALIK LAYAR:
        // Sangat penting untuk mengembalikan timeScale ke 1.0f sebelum berpindah ke Main Menu,
        // jika tidak, UI, tombol, atau animasi di Main Menu tidak akan bisa berinteraksi karena ter-pause.
        Time.timeScale = 1f;
        
        // Memuat scene menu utama menggunakan nama scene yang dikonfigurasi di Inspector.
        UnityEngine.SceneManagement.SceneManager.LoadScene(mainMenuSceneName);
        
        Debug.Log($"[HUDController] Loading Main Menu scene: '{mainMenuSceneName}'.");
    }
}
