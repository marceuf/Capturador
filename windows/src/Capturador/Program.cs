using System.Windows;

namespace Capturador;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        using var single = new Mutex(true, "Capturador-tech.marceu.capturador", out var first);
        if (!first)
        {
            MessageBox.Show("O Capturador já está aberto. O ícone fica na bandeja, ao lado do relógio.",
                            "Capturador", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var lastError = DateTime.MinValue;
        app.DispatcherUnhandledException += (_, e) =>
        {
            // Um erro numa ação não pode levar embora as capturas abertas.
            e.Handled = true;
            // Um erro que se repete a cada quadro não pode virar uma fila de avisos.
            if (DateTime.Now - lastError < TimeSpan.FromSeconds(3)) return;
            lastError = DateTime.Now;
            MessageBox.Show("Algo deu errado: " + e.Exception.Message, "Capturador",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        using var host = new AppHost();
        app.Startup += (_, _) => host.Start();
        return app.Run();
    }
}
