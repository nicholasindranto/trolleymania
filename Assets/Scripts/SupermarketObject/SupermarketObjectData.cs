using UnityEngine;

/// <summary>
/// ScriptableObject ini bertindak sebagai template data/metadata statis untuk setiap barang di supermarket.
/// Dengan menggunakan ScriptableObject, kita menghemat memori secara signifikan karena ratusan objek fisik
/// yang sama di scene (misalnya: 50 kaleng susu) hanya akan merujuk pada satu berkas aset data ini di memori RAM,
/// bukan membuat duplikat data statis baru untuk setiap objeknya.
/// </summary>
[CreateAssetMenu(fileName = "NewSupermarketObject", menuName = "TrolleyMania/Supermarket Object Data")]
public class SupermarketObjectData : ScriptableObject
{
    [Header("Static Metadata")]
    [Tooltip("Nama identitas dari barang/objek.")]
    public string objName;

    [Tooltip("Berat fisik dari barang/objek.")]
    public float objWeight;



    [Tooltip("Status awal bawaan objek saat pertama kali di-spawn.")]
    public ObjectStatus defaultStatus = ObjectStatus.Grounded;

    [Tooltip("Ikon visual objek untuk ditampilkan di list UI (PUBG Mobile style).")]
    public Sprite objIcon;
}
