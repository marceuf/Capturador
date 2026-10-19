namespace Capturador.Core;

/// <summary>
/// Desfoque que esconde de verdade. Só pixelizar deixa cada bloco com a média exata do trecho
/// embaixo, o que permite reconstruir texto comparando com fontes conhecidas; o borrão antes
/// mistura os vizinhos e desfaz essa correspondência. Mesma regra do Mac.
/// </summary>
public static class Redaction
{
    public static int BlockSize(int width, int height) => Math.Max(24, Math.Max(width, height) / 40);

    /// <summary>Imagem BGRA borrada e pixelizada, do mesmo tamanho, com stride compacto (largura × 4).</summary>
    public static byte[] Pixelate(byte[] bgra, int width, int height, int stride)
    {
        var block = BlockSize(width, height);
        var blurred = Blur(bgra, width, height, stride, block / 2);
        var output = new byte[width * height * 4];
        for (int by = 0; by < height; by += block)
            for (int bx = 0; bx < width; bx += block)
            {
                int x1 = Math.Min(bx + block, width), y1 = Math.Min(by + block, height);
                long b = 0, g = 0, r = 0, a = 0;
                for (int y = by; y < y1; y++)
                    for (int x = bx; x < x1; x++)
                    {
                        int i = (y * width + x) * 4;
                        b += blurred[i]; g += blurred[i + 1]; r += blurred[i + 2]; a += blurred[i + 3];
                    }
                long n = (long)(x1 - bx) * (y1 - by);
                byte cb = (byte)(b / n), cg = (byte)(g / n), cr = (byte)(r / n), ca = (byte)(a / n);
                for (int y = by; y < y1; y++)
                    for (int x = bx; x < x1; x++)
                    {
                        int i = (y * width + x) * 4;
                        output[i] = cb; output[i + 1] = cg; output[i + 2] = cr; output[i + 3] = ca;
                    }
            }
        return output;
    }

    /// <summary>Três passadas de média móvel em cada eixo, aproximando um borrão gaussiano de raio <paramref name="radius"/>.</summary>
    private static byte[] Blur(byte[] src, int width, int height, int stride, int radius)
    {
        var a = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(src, y * stride, a, y * width * 4, width * 4);
        var b = new byte[a.Length];
        int pass = Math.Max(1, radius / 2);
        for (int i = 0; i < 3; i++)
        {
            BoxPass(a, b, width, height, pass, horizontal: true);
            BoxPass(b, a, width, height, pass, horizontal: false);
        }
        return a;
    }

    private static void BoxPass(byte[] src, byte[] dst, int width, int height, int r, bool horizontal)
    {
        int lines = horizontal ? height : width, length = horizontal ? width : height;
        int window = 2 * r + 1;
        Span<int> sum = stackalloc int[4];
        for (int line = 0; line < lines; line++)
        {
            int Index(int k) => horizontal
                ? (line * width + Math.Clamp(k, 0, length - 1)) * 4
                : (Math.Clamp(k, 0, length - 1) * width + line) * 4;
            sum.Clear();
            for (int k = -r; k <= r; k++)
            {
                int i = Index(k);
                for (int c = 0; c < 4; c++) sum[c] += src[i + c];
            }
            for (int k = 0; k < length; k++)
            {
                int o = Index(k);
                for (int c = 0; c < 4; c++) dst[o + c] = (byte)(sum[c] / window);
                int add = Index(k + r + 1), remove = Index(k - r);
                for (int c = 0; c < 4; c++) sum[c] += src[add + c] - src[remove + c];
            }
        }
    }
}
