using input_startup_project;
using Microsoft.VisualStudio.Shell;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using System;
using System.Windows.Input;

internal sealed class isp
{
    public const int CommandId = 0x0100;
    public static readonly Guid CommandSet = new Guid("9d9a7a1d-7be3-4c00-9c24-d2841fc436a6");
    private readonly AsyncPackage package;
    private static System.Windows.Window inputWindow;

    private isp(AsyncPackage package, OleMenuCommandService commandService)
    {
        this.package = package ?? throw new ArgumentNullException(nameof(package));
        commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

        var menuCommandID = new CommandID(CommandSet, CommandId);
        var menuItem = new MenuCommand(this.Execute, menuCommandID);
        commandService.AddCommand(menuItem);
    }

    public static isp Instance { get; private set; }

    private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
    {
        get { return this.package; }
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        Instance = new isp(package, commandService);
    }

    private async void Execute(object sender, EventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var inputControl = new InputControl();
        inputWindow = new System.Windows.Window
        {
            Title = "设置启动项 (按 Esc 关闭窗口)",
            Content = inputControl,
            Width = 720,
            Height = 480,
            Topmost = true
        };

        EnvDTE.DTE dte = (EnvDTE.DTE)await ServiceProvider.GetServiceAsync(typeof(EnvDTE.DTE));
        IntPtr mainWindowHandle = dte.MainWindow.HWnd;
        new System.Windows.Interop.WindowInteropHelper(inputWindow).Owner = mainWindowHandle;

        if (GetWindowRect(mainWindowHandle, out RECT rect))
        {
            inputWindow.Left = rect.Left + 128;
            inputWindow.Top = rect.Top + 128;
        }

        inputWindow.PreviewKeyDown += (s, args) =>
        {
            if (args.Key == Key.Escape)
            {
                inputWindow.Close();
            }
        };

        inputWindow.ShowDialog();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
}
