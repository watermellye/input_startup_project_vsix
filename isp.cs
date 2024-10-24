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

        if (inputWindow == null)
        {
            var inputControl = new InputControl();
            inputWindow = new System.Windows.Window
            {
                Title = "设置启动项 (按 Esc 关闭窗口)",
                Content = inputControl,
                Width = 720,
                Height = 480,
                Topmost = true
            };

            inputWindow.Closed += (s, a) => inputWindow = null;

            // 使用 lambda 表达式处理 PreviewKeyDown 事件
            inputWindow.PreviewKeyDown += (s, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    inputWindow.Close();
                }
            };
        }

        inputWindow.ShowDialog();
        //inputWindow.Show();
        //inputWindow.Activate(); // Bring the existing window to the front.
        //inputWindow.Focus();
    }
}
