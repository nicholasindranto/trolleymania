using UnityEngine;

/// <summary>
/// ScriptableObject untuk menyimpan konfigurasi lore/sifat dasar dari NPC.
/// Mempermudah penyesuaian sifat dan lore NPC melalui Unity Inspector tanpa menyentuh kode program.
/// </summary>
[CreateAssetMenu(fileName = "NewNPCLorePreset", menuName = "TrolleyMania/NPC Lore Preset", order = 1)]
public class NPCLorePreset : ScriptableObject
{
    [Header("Speed Settings")]
    [Tooltip("Kecepatan gerak minimum saat berpatroli (m/s).")]
    public float activeMinSpeed = 5.0f;
    [Tooltip("Kecepatan gerak maksimum saat berpatroli (m/s).")]
    public float activeMaxSpeed = 6.0f;

    [Header("Stay Settings")]
    [Tooltip("Lama waktu diam minimum saat sampai di stay point (detik).")]
    public float activeMinStay = 5.0f;
    [Tooltip("Lama waktu diam maksimum saat sampai di stay point (detik).")]
    public float activeMaxStay = 6.0f;

    [Header("Physics & Handling")]
    [Tooltip("Akselerasi pergerakan troli NPC.")]
    public float acceleration = 6.0f;
    [Tooltip("Deselerasi pergerakan troli NPC.")]
    public float deceleration = 8.0f;
    [Tooltip("Sensivitas putaran/belok NPC.")]
    public float turnSensitivity = 1.5f;
}
