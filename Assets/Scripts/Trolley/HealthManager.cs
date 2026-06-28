using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Script ini berfungsi sebagai pengelola status kesehatan (Player HP dan Trolley Durability).
/// Bertanggung jawab atas pengelolaan kapasitas darah, perubahan visual transparansi di HUD UI, 
/// serta memicu panel Game Over jika salah satu dari health/durability habis.
/// </summary>
public class HealthManager : MonoBehaviour
{
    public static HealthManager Instance { get; private set; }

    [Header("Health Settings")]
    [Tooltip("Jumlah darah maksimal Player (hati).")]
    [SerializeField] private int maxPlayerHp = 5;
    
    [Tooltip("Jumlah durabilitas maksimal Trolley (kunci inggris).")]
    [SerializeField] private int maxTrolleyDurability = 8;

    [Header("UI Containers")]
    [Tooltip("Parent GameObject dari icon hati player (healthicon).")]
    [SerializeField] private Transform playerHpContainer;

    [Tooltip("Parent GameObject dari icon kunci inggris trolley (wrenchicon).")]
    [SerializeField] private Transform trolleyDurabilityContainer;

    [Header("Game Over UI")]
    [Tooltip("Panel UI Game Over (BGYouLose) yang akan aktif ketika HP/Durability habis.")]
    [SerializeField] private GameObject bgYouLosePanel;

    [Header("UI Alpha Configs")]
    [Tooltip("Tingkat kemurnian warna / opacity (alpha) saat bar masih aktif (penuh).")]
    [Range(0f, 1f)]
    [SerializeField] private float activeAlpha = 1.0f;

    [Tooltip("Tingkat kemurnian warna / opacity (alpha) saat bar sudah kosong/pecah.")]
    [Range(0f, 1f)]
    [SerializeField] private float inactiveAlpha = 0.2f;

    [Header("UI Hierarchy Matching Configs")]
    [Tooltip("Prefix nama objek penampung (container) bar di Canvas (misal 'BG' untuk 'BGhealth').")]
    [SerializeField] private string bgContainerNamePrefix = "BG";

    [Tooltip("Nama GameObject gambar hati merah pengisi darah di dalam container.")]
    [SerializeField] private string filledHpChildName = "health";

    [Tooltip("Nama GameObject gambar kunci inggris biru pengisi durabilitas di dalam container.")]
    [SerializeField] private string filledDurabilityChildName = "durab";

    // Nilai status saat ini
    private int currentPlayerHp;
    private int currentTrolleyDurability;

    // Properti publik untuk diakses oleh ScoreManager
    public int CurrentPlayerHp => currentPlayerHp;
    public int MaxPlayerHp => maxPlayerHp;
    public int CurrentTrolleyDurability => currentTrolleyDurability;
    public int MaxTrolleyDurability => maxTrolleyDurability;

    // List internal untuk menampung referensi Image HUD agar tidak perlu drag & drop satu per satu
    private List<Image> playerHpImages = new List<Image>();
    private List<Image> trolleyDurabilityImages = new List<Image>();

    private void Awake()
    {
        // LOGIC DI BALIK LAYAR (Singleton Pattern):
        // Membuat static Instance agar script pendeteksi tabrakan (collision handler)
        // dapat mengurangi HP/durabilitas secara instan tanpa pencarian manual yang berat.
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Setel HP dan Durabilitas ke nilai penuh saat inisialisasi awal
        currentPlayerHp = maxPlayerHp;
        currentTrolleyDurability = maxTrolleyDurability;
    }

    private void Start()
    {
        // LOGIC DI BALIK LAYAR (Automatic Image Caching):
        // Kita memindai anak-anak transform dari kontainer secara dinamis untuk mengambil komponen Image.
        // Ini menghindari repotnya men-drag 12 slot gambar secara manual di Inspector Unity.
        CacheUIImages();

        // Pastikan panel kalah (BGYouLose) mati saat awal permainan
        if (bgYouLosePanel != null)
        {
            bgYouLosePanel.SetActive(false);
        }

        // Perbarui visual UI pertama kali
        UpdateHpUI();
        UpdateDurabilityUI();
    }

    /// <summary>
    /// Mencari dan mengelompokkan Image penunjuk HP dan Durability secara otomatis.
    /// </summary>
    private void CacheUIImages()
    {
        if (playerHpContainer != null)
        {
            foreach (Transform child in playerHpContainer)
            {
                // Cari container yang namanya mengandung prefix tertentu
                if (child.name.Contains(bgContainerNamePrefix))
                {
                    // Cari objek anak yang merupakan gambar pengisi darah
                    Transform filledChild = child.Find(filledHpChildName);
                    if (filledChild != null)
                    {
                        Image img = filledChild.GetComponent<Image>();
                        if (img != null)
                        {
                            playerHpImages.Add(img);
                        }
                    }
                }
            }
        }

        if (trolleyDurabilityContainer != null)
        {
            foreach (Transform child in trolleyDurabilityContainer)
            {
                // Cari container yang namanya mengandung prefix tertentu
                if (child.name.Contains(bgContainerNamePrefix))
                {
                    // Cari objek anak yang merupakan gambar pengisi durabilitas
                    Transform filledChild = child.Find(filledDurabilityChildName);
                    if (filledChild != null)
                    {
                        Image img = filledChild.GetComponent<Image>();
                        if (img != null)
                        {
                            trolleyDurabilityImages.Add(img);
                        }
                    }
                }
            }
        }

        Debug.Log($"[HealthManager] Berhasil mendeteksi {playerHpImages.Count} HP Bars dan {trolleyDurabilityImages.Count} Durability Bars di Canvas.");
    }

    /// <summary>
    /// Mengurangi Player HP secara aman.
    /// </summary>
    public void ReducePlayerHp(int amount)
    {
        if (currentPlayerHp <= 0) return;

        currentPlayerHp = Mathf.Max(0, currentPlayerHp - amount);
        Debug.Log($"[HealthManager] Player HP berkurang {amount}. Sisa HP: {currentPlayerHp}");
        
        UpdateHpUI();

        // Periksa kondisi kekalahan
        if (currentPlayerHp <= 0)
        {
            TriggerGameOver();
        }
    }

    /// <summary>
    /// Mengurangi Durabilitas Trolley secara aman.
    /// </summary>
    public void ReduceTrolleyDurability(int amount)
    {
        if (currentTrolleyDurability <= 0) return;

        currentTrolleyDurability = Mathf.Max(0, currentTrolleyDurability - amount);
        Debug.Log($"[HealthManager] Trolley Durability berkurang {amount}. Sisa Durabilitas: {currentTrolleyDurability}");
        
        UpdateDurabilityUI();

        // Periksa kondisi kekalahan
        if (currentTrolleyDurability <= 0)
        {
            TriggerGameOver();
        }
    }

    /// <summary>
    /// Memperbarui tingkat transparansi warna merah pada icon hati Player.
    /// </summary>
    private void UpdateHpUI()
    {
        // LOGIC DI BALIK LAYAR:
        // Setiap bar/hati yang masih aktif (indeks < currentPlayerHp) diberi warna solid (alpha = activeAlpha).
        // Bar yang sudah pecah/berkurang diberi warna transparan redup (alpha = inactiveAlpha).
        for (int i = 0; i < playerHpImages.Count; i++)
        {
            if (playerHpImages[i] != null)
            {
                Color col = playerHpImages[i].color;
                col.a = (i < currentPlayerHp) ? activeAlpha : inactiveAlpha;
                playerHpImages[i].color = col;
            }
        }
    }

    /// <summary>
    /// Memperbarui tingkat transparansi warna biru pada icon kunci inggris Trolley.
    /// </summary>
    private void UpdateDurabilityUI()
    {
        for (int i = 0; i < trolleyDurabilityImages.Count; i++)
        {
            if (trolleyDurabilityImages[i] != null)
            {
                Color col = trolleyDurabilityImages[i].color;
                col.a = (i < currentTrolleyDurability) ? activeAlpha : inactiveAlpha;
                trolleyDurabilityImages[i].color = col;
            }
        }
    }

    /// <summary>
    /// Memicu Game Over (Kekalahan), menghentikan jalannya game dan mengaktifkan panel BGYouLose.
    /// </summary>
    private void TriggerGameOver()
    {
        Debug.LogWarning("[HealthManager] Game Over! HP atau Durabilitas habis.");

        // 1. Tampilkan panel kekalahan
        if (bgYouLosePanel != null)
        {
            bgYouLosePanel.SetActive(true);
        }

        // 2. LOGIC DI BALIK LAYAR:
        //    Menyetel Time.timeScale ke 0f membekukan jalannya fisika (FixedUpdate) dan pergerakan, 
        //    menghentikan permainan seketika sehingga player tidak bisa bergerak lagi.
        Time.timeScale = 0f;
    }
}
