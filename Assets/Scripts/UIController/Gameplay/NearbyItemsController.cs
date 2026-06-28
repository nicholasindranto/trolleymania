using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mengontrol daftar UI barang di sekitar pemain (Nearby Items PUBG Mobile style).
/// Menggunakan sistem Object Pool untuk mencegah overhead memori (GC Alloc) pada Mobile WebGL.
/// </summary>
public class NearbyItemsController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Referensi ke TrolleyInteractController untuk mendeteksi barang terdekat.")]
    [SerializeField] private TrolleyInteractController interactController;

    [Tooltip("Referensi ke HUDController untuk memicu proses Grab barang.")]
    [SerializeField] private HUDController hudController;

    [Header("UI Layout References")]
    [Tooltip("Panel penampung utama UI list (biasanya memiliki ScrollRect / CanvasGroup).")]
    [SerializeField] private GameObject mainPanel;

    [Tooltip("Transform Content (Viewport Content) tempat tombol-tombol item di-spawn (Vertical Layout Group).")]
    [SerializeField] private Transform contentContainer;

    [Header("Prefabs & Assets")]
    [Tooltip("Prefab tombol item baris (NearbyItemButton).")]
    [SerializeField] private NearbyItemButton itemButtonPrefab;

    [Tooltip("Ikon default jika barang di supermarket belum dikonfigurasikan ikonnya di ScriptableObject.")]
    [SerializeField] private Sprite defaultItemIcon;

    // Pool tombol UI untuk didaur ulang secara instan tanpa instansiasi/penghancuran dinamis
    private readonly List<NearbyItemButton> buttonPool = new List<NearbyItemButton>();

    private void Start()
    {
        // Validasi referensi
        if (interactController == null)
        {
            interactController = FindObjectOfType<TrolleyInteractController>();
        }

        if (hudController == null)
        {
            hudController = FindObjectOfType<HUDController>();
        }

        // Daftarkan listener ke event-driven system agar UI hanya diperbarui saat ada barang masuk/keluar
        if (interactController != null)
        {
            interactController.OnCandidatesChanged += RefreshUI;
        }

        // Panggilan inisialisasi awal
        RefreshUI();
    }

    private void OnDestroy()
    {
        // Bersihkan listener event untuk menghindari memory leak (kebocoran memori)
        if (interactController != null)
        {
            interactController.OnCandidatesChanged -= RefreshUI;
        }
    }

    /// <summary>
    /// Memperbarui rendering list UI berdasarkan data terkini dari TrolleyInteractController.
    /// </summary>
    public void RefreshUI()
    {
        if (interactController == null || contentContainer == null || itemButtonPrefab == null)
        {
            if (mainPanel != null) mainPanel.SetActive(false);
            return;
        }

        int candidateCount = interactController.CandidateCount;

        // 1. Tampilkan atau sembunyikan panel utama berdasarkan ketersediaan barang di sekitar
        if (mainPanel != null)
        {
            mainPanel.SetActive(candidateCount > 0);
        }

        // 2. Nonaktifkan semua tombol aktif yang ada di pool terlebih dahulu (daur ulang)
        for (int i = 0; i < buttonPool.Count; i++)
        {
            if (buttonPool[i] != null)
            {
                buttonPool[i].gameObject.SetActive(false);
            }
        }

        // 3. Render tombol baru dari data kandidat aktif menggunakan pool
        for (int i = 0; i < candidateCount; i++)
        {
            ObjectScript candidateScript = interactController.GetCandidateObjectScript(i);
            if (candidateScript == null) continue;

            NearbyItemButton buttonInstance;

            // Gunakan tombol yang sudah ada di pool jika tersedia
            if (i < buttonPool.Count)
            {
                buttonInstance = buttonPool[i];
            }
            else
            {
                // Spawn tombol baru hanya jika pool tidak mencukupi
                buttonInstance = Instantiate(itemButtonPrefab, contentContainer);
                buttonPool.Add(buttonInstance);
            }

            if (buttonInstance != null)
            {
                buttonInstance.gameObject.SetActive(true);
                // Tangkap referensi candidateScript lokal ke closure untuk WebGL safety
                ObjectScript currentItem = candidateScript;
                buttonInstance.Initialize(currentItem, () => hudController.GrabObject(currentItem), defaultItemIcon);
            }
        }
    }
}
