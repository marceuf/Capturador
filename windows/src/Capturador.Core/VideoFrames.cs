namespace Capturador.Core;

/// <summary>
/// Conversão dos quadros da tela (BGRA) para NV12, o formato que o codificador H.264 do Windows
/// usa. Entregar BGRA e deixar a conversão com o Media Foundation gerou vídeo verde e rosa.
/// </summary>
public static class VideoFrames
{
    public static int Nv12Size(int width, int height) => width * height * 3 / 2;

    /// <summary>BT.601, faixa limitada (16–235), cor em 2×2. Largura e altura precisam ser pares.</summary>
    public static void BgraToNv12(byte[] bgra, int width, int height, byte[] nv12)
    {
        if (width % 2 != 0 || height % 2 != 0) throw new ArgumentException("Largura e altura precisam ser pares.");
        int uvStart = width * height;
        for (int y = 0; y < height; y += 2)
        {
            int row0 = y * width, row1 = row0 + width;
            int uvRow = uvStart + (y / 2) * width;
            for (int x = 0; x < width; x += 2)
            {
                int r = 0, g = 0, b = 0;
                foreach (var p in (ReadOnlySpan<int>)[row0 + x, row0 + x + 1, row1 + x, row1 + x + 1])
                {
                    int i = p * 4;
                    int pb = bgra[i], pg = bgra[i + 1], pr = bgra[i + 2];
                    nv12[p] = (byte)(((66 * pr + 129 * pg + 25 * pb + 128) >> 8) + 16);
                    r += pr; g += pg; b += pb;
                }
                r >>= 2; g >>= 2; b >>= 2;
                nv12[uvRow + x] = (byte)(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);
                nv12[uvRow + x + 1] = (byte)(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);
            }
        }
    }
}
