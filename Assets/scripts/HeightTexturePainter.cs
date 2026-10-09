using UnityEngine;

/// <summary>
/// Verft een Terrain Layer (texture) automatisch op alle plekken van het terrein
/// die binnen een opgegeven hoogtebereik liggen.
/// Gebruik: zet dit script op je Terrain-object, stel de waarden in en klik
/// rechts op de component-header (of op de drie puntjes) > "Pas texture toe op hoogte".
/// </summary>
[RequireComponent(typeof(Terrain))]
public class HeightTexturePainter : MonoBehaviour
{
    [Tooltip("Index van de Terrain Layer, in de volgorde zoals ze in Paint Texture staan (eerste = 0).")]
    public int layerIndex = 1;

    [Tooltip("Ondergrens in wereld-hoogte (Y), in meters.")]
    public float minHeight = 0f;

    [Tooltip("Bovengrens in wereld-hoogte (Y), in meters.")]
    public float maxHeight = 10f;

    [Tooltip("Breedte van de zachte overgang aan beide kanten, in meters. 0 = harde rand.")]
    public float blend = 1f;

    [ContextMenu("Pas texture toe op hoogte")]
    public void Apply()
    {
        Terrain terrain = GetComponent<Terrain>();
        TerrainData data = terrain.terrainData;

        int width = data.alphamapWidth;
        int height = data.alphamapHeight;
        int layers = data.alphamapLayers;

        if (layerIndex < 0 || layerIndex >= layers)
        {
            Debug.LogError($"Layer index {layerIndex} bestaat niet. Dit terrein heeft {layers} Terrain Layer(s).");
            return;
        }

#if UNITY_EDITOR
        UnityEditor.Undo.RegisterCompleteObjectUndo(data, "Height-based texture");
#endif

        float terrainY = terrain.GetPosition().y;
        float[,,] maps = data.GetAlphamaps(0, 0, width, height);

        // Let op: alphamap-index is [z, x, layer]
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)(width - 1);
                float nz = z / (float)(height - 1);
                float worldHeight = terrainY + data.GetInterpolatedHeight(nx, nz);

                float weight = Weight(worldHeight);
                if (weight <= 0f) continue;

                // Nieuwe layer erbij mengen, andere layers evenredig verlagen (totaal blijft 1)
                for (int l = 0; l < layers; l++)
                {
                    if (l == layerIndex)
                        maps[z, x, l] = maps[z, x, l] * (1f - weight) + weight;
                    else
                        maps[z, x, l] *= (1f - weight);
                }
            }
        }

        data.SetAlphamaps(0, 0, maps);
        Debug.Log($"Texture-layer {layerIndex} toegepast tussen {minHeight} m en {maxHeight} m.");
    }

    private float Weight(float h)
    {
        if (blend <= 0f)
            return (h >= minHeight && h <= maxHeight) ? 1f : 0f;

        float bottom = Mathf.InverseLerp(minHeight - blend, minHeight, h);
        float top = Mathf.InverseLerp(maxHeight + blend, maxHeight, h);
        return Mathf.Min(bottom, top);
    }
}
