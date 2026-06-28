using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Mengontrol komponen baris/tombol item tunggal dalam list PUBG Mobile Style (Nearby Items).
/// </summary>
public class NearbyItemButton : MonoBehaviour
{
    [Header("UI Component References")]
    [Tooltip("Komponen Image untuk menampilkan ikon item.")]
    [SerializeField] private Image iconImage;

    [Tooltip("Komponen Text untuk menampilkan nama item.")]
    [SerializeField] private TextMeshProUGUI nameText;

    [Tooltip("Komponen Text untuk menampilkan berat item.")]
    [SerializeField] private TextMeshProUGUI weightText;

    [Tooltip("Komponen Button utama untuk interaksi grab.")]
    [SerializeField] private Button actionButton;

    // Referensi internal ke ObjectScript
    private ObjectScript targetObjectScript;

    /// <summary>
    /// Menginisialisasi data tombol baris item ini secara decoupled (fleksibel untuk list Nearby maupun Inventory).
    /// </summary>
    /// <param name="objScript">Script objek supermarket yang dideteksi.</param>
    /// <param name="onClickAction">Fungsi delegate yang dijalankan ketika tombol diklik.</param>
    /// <param name="fallbackSprite">Ikon default jika item tidak memiliki ikon khusus di ScriptableObject.</param>
    public void Initialize(ObjectScript objScript, System.Action onClickAction, Sprite fallbackSprite)
    {
        targetObjectScript = objScript;

        if (targetObjectScript != null && targetObjectScript.ObjectData != null)
        {
            // Ambil data metadata dari ScriptableObject secara aman
            nameText.text = targetObjectScript.ObjectData.objName;
            weightText.text = $"{targetObjectScript.ObjectData.objWeight:F1} kg";

            // Set ikon PUBG style
            if (targetObjectScript.ObjectData.objIcon != null)
            {
                iconImage.sprite = targetObjectScript.ObjectData.objIcon;
                iconImage.enabled = true;
            }
            else if (fallbackSprite != null)
            {
                iconImage.sprite = fallbackSprite;
                iconImage.enabled = true;
            }
            else
            {
                iconImage.enabled = false;
            }
        }

        // Daftarkan listener klik tombol secara bersih menggunakan delegate eksternal
        actionButton.onClick.RemoveAllListeners();
        if (onClickAction != null)
        {
            actionButton.onClick.AddListener(() => onClickAction());
        }
    }
}
