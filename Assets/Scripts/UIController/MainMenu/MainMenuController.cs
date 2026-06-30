using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Script ini berfungsi sebagai pengendali utama UI Menu Utama (Main Menu).
/// Menangani perpindahan scene ke gameplay, membuka/menutup panel informasi (How To Play & Credit),
/// serta keluar dari permainan.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Scene Settings")]
    [Tooltip("Nama scene gameplay yang akan dimuat saat tombol Start ditekan.")]
    [SerializeField] private string gameplaySceneName = "SampleScene";

    [Header("Optional UI Panels")]
    [Tooltip("Panel instruksi bermain (How To Play). Bersifat opsional.")]
    [SerializeField] private GameObject howToPlayPanel;

    [Tooltip("Panel pembuat game (Credit). Bersifat opsional.")]
    [SerializeField] private GameObject creditPanel;

    private void Start()
    {
        // LOGIC DI BALIK LAYAR:
        // Memastikan timeScale disetel ke 1.0f saat masuk ke Main Menu.
        // Ini sangat penting jika pemain kembali ke menu utama dari kondisi game ter-pause (timeScale = 0).
        Time.timeScale = 1f;

        // Sembunyikan panel-panel opsional di awal agar menu utama bersih
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
        if (creditPanel != null) creditPanel.SetActive(false);
    }

    /// <summary>
    /// Memulai permainan dengan memuat scene gameplay.
    /// Dihubungkan ke Event On Click tombol "Start".
    /// </summary>
    public void PlayGame()
    {
        Debug.Log($"[MainMenuController] Memuat scene gameplay: '{gameplaySceneName}'");
        
        // Memastikan waktu berjalan normal sebelum memuat scene baru
        Time.timeScale = 1f;
        
        SceneManager.LoadSceneAsync(gameplaySceneName);
    }

    /// <summary>
    /// Membuka atau menutup panel panduan bermain (How To Play).
    /// Dihubungkan ke Event On Click tombol "How To Play" dan tombol "Back/Close" di dalam panel tersebut.
    /// </summary>
    public void ToggleHowToPlay(bool active)
    {
        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(active);
            Debug.Log($"[MainMenuController] Set HowToPlay Panel active = {active}");
        }
        else
        {
            Debug.LogWarning("[MainMenuController] Panel HowToPlay belum di-assign di Inspector!");
        }
    }

    /// <summary>
    /// Membuka atau menutup panel pembuat game (Credit).
    /// Dihubungkan ke Event On Click tombol "Credit" dan tombol "Back/Close" di dalam panel tersebut.
    /// </summary>
    public void ToggleCredit(bool active)
    {
        if (creditPanel != null)
        {
            creditPanel.SetActive(active);
            Debug.Log($"[MainMenuController] Set Credit Panel active = {active}");
        }
        else
        {
            Debug.LogWarning("[MainMenuController] Panel Credit belum di-assign di Inspector!");
        }
    }

    /// <summary>
    /// Keluar dari game.
    /// Dihubungkan ke Event On Click tombol "Quit".
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("[MainMenuController] Keluar dari permainan.");

        // LOGIC DI BALIK LAYAR:
        // Application.Quit() hanya bekerja pada build akhir (APK Android, EXE PC, dsb).
        // Agar tombol Quit juga bekerja saat diuji di dalam Unity Editor, 
        // kita gunakan preprocessor directive untuk mematikan mode Play.
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }
}
