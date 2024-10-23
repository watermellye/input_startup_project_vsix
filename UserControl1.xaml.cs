using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.Settings;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace input_startup_project
{
    public class ProjectInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public Project Project { get; set; }

        public ProjectInfo(string name, string path, Project project)
        {
            Name = name;
            Path = path;
            Project = project;
        }
    }

    public partial class InputControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private DTE2 _dte;
        private Dictionary<string, ProjectInfo> _projectName2Info;

        private WritableSettingsStore _userSettingsStore;
        private const string _collectionPath = "InputStartupProject";
        private const string _lastInputTextPropertyName = "LastInputText";

        private string _currentSelectedProjectName = null;

        public string CurrentSelectedProjectName
        {
            get
            {
                return _currentSelectedProjectName;
            }
            set
            {
                if (_currentSelectedProjectName == value) return;
                _currentSelectedProjectName = value;
                OnPropertyChanged(nameof(NavigateToButtonContent));
                OnPropertyChanged(nameof(NavigateToButtonEnabled));
                OnPropertyChanged(nameof(SetAsStartupProjectButtonContent));
                OnPropertyChanged(nameof(SetAsStartupProjectButtonEnabled));
            }
        }

        public string NavigateToButtonContent => $"将解决方案资源管理器中导航至 {CurrentSelectedProjectName ?? "..."}";
        public bool NavigateToButtonEnabled => !string.IsNullOrEmpty(CurrentSelectedProjectName);
        public string SetAsStartupProjectButtonContent => $"将 {CurrentSelectedProjectName ?? "..."} 设为启动项 (Enter)";
        public bool SetAsStartupProjectButtonEnabled => !string.IsNullOrEmpty(CurrentSelectedProjectName);

        public InputControl()
        {
            InitializeComponent();
            DataContext = this;
            InitializeSettingsStore();
            InitializeDteAndProjects();
            ProjectNameTextBox.Text = LoadInputText(); // 恢复上次的数据
            HandleTextChanged(ProjectNameTextBox.Text);
            ProjectNameTextBox.Focus();
        }

        private void InitializeSettingsStore()
        {
            var shellSettingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
            _userSettingsStore = shellSettingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

            
            if (!_userSettingsStore.CollectionExists(_collectionPath))
            {
                _userSettingsStore.CreateCollection(_collectionPath);
            }
        }

        private void SaveInputText(string text)
        {
            _userSettingsStore.SetString(_collectionPath, _lastInputTextPropertyName, text);
        }

        private string LoadInputText()
        {
            if (_userSettingsStore.PropertyExists(_collectionPath, _lastInputTextPropertyName))
            {
                return _userSettingsStore.GetString(_collectionPath, _lastInputTextPropertyName);
            }
            return string.Empty;
        }

        private async void InitializeDteAndProjects()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is DTE2 dte)
            {
                _dte = dte;
                _projectName2Info = new Dictionary<string, ProjectInfo>();
                Solution2 solution = _dte.Solution as Solution2;

                foreach (Project project in solution.Projects)
                {
                    GetProjectNames(project, project.Name);
                }
            }

            if (_dte == null)
            {
                ShowInternalError("cannot fetch DTE");
                return;
            }
        }

        private void GetProjectNames(Project project, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null)
                return;

            if (project.Kind != ProjectKinds.vsProjectKindSolutionFolder)
            {
                _projectName2Info[project.Name] = new ProjectInfo(project.Name, path, project);
                return;
            }

            foreach (ProjectItem projectItem in project.ProjectItems)
            {
                Project subProject = projectItem.SubProject;
                if (string.IsNullOrEmpty(subProject?.Name)) continue;
                GetProjectNames(subProject, path + '/' + subProject.Name);
            }
        }

        private async void ShowInternalError(string message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            VsShellUtilities.ShowMessageBox(
                    ServiceProvider.GlobalProvider,
                    $"Internal error: {message}",
                    "Error",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private async void OnSetAsStartupProjectButtonClick(object sender, RoutedEventArgs e)
        {
            _ = await SetAsStartupProject();
        }

        private async Task<bool> SetAsStartupProject()
        {
            string projectName = CurrentSelectedProjectName;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (_dte == null)
                return false;

            if (_projectName2Info.TryGetValue(projectName, out ProjectInfo projectInfo))
            {
                _dte.Solution.Properties.Item("StartupProject").Value = projectName;
                return true;
            }
            else
            {
                ShowInternalError($"[{ProjectNameTextBox.Text} not found");
                return false;
            }
        }

        private async void OnNavigateToButtonClick(object sender, RoutedEventArgs e)
        {
            _ = await NavigateTo();
        }

        private async Task<bool> NavigateTo()
        {
            string projectName = CurrentSelectedProjectName;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (_dte == null)
                return false;

            if (_projectName2Info.TryGetValue(projectName, out ProjectInfo projectInfo))
            {
                UIHierarchyItems rootNodes = _dte.ToolWindows.SolutionExplorer?.UIHierarchyItems;
                if (rootNodes == null)
                {
                    ShowInternalError($"[errorcode=1] Cannot navigate to {projectInfo.Path}");
                    return false;
                }
                foreach (object _node in rootNodes)
                {
                    UIHierarchyItem node = _node as UIHierarchyItem;
                    foreach (string path in projectInfo.Path.Split('/'))
                    {
                        UIHierarchyItem subnode = null;
                        foreach (UIHierarchyItem item in node.UIHierarchyItems)
                        {
                            if (item.Name == path)
                            {
                                subnode = item;
                                break;
                            }
                        }
                        if (subnode == null)
                        {
                            node = null;
                            break;
                        }
                        node = subnode;
                    }
                    if (node != null)
                    {
                        node.Select(vsUISelectionType.vsUISelectionTypeSelect);
                        return true;
                    }
                }
                ShowInternalError($"[errorcode=2] Cannot navigate to {projectInfo.Path}");
                return false;
            }
            else
            {
                ShowInternalError($"{projectName} not found");
                return false;
            }
        }

        private async void ProjectNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                SaveInputText(textBox.Text);
                HandleTextChanged(textBox.Text);
            }
        }

        private async void HandleTextChanged(string currentText)
        {
            List<string> keywords = Regex.Split(currentText?.Trim() ?? "", @"\s+").Where(s => !string.IsNullOrEmpty(s)).Select(s => s.ToLower()).ToList();
            if (keywords.Count == 0)
            {
                CurrentSelectedProjectName = null;
                SetTextToRichTextBox(_projectName2Info.Values.ToList(), null, "所有项目：");
            }
            else
            {
                List<string> matchedProjectNames = _projectName2Info.Keys
                .Where(projectName => keywords.All(word => projectName.ToLower().Contains(word)))
                .ToList();

                if (matchedProjectNames.Count == 0)
                {
                    CurrentSelectedProjectName = null;
                    SetTextToRichTextBox(null, null, "没有符合搜索条件的项目");
                }
                else
                {
                    matchedProjectNames.Sort();
                    CurrentSelectedProjectName = matchedProjectNames[0];
                    SetTextToRichTextBox(matchedProjectNames.Select(s => _projectName2Info[s]).ToList(), keywords, $"你要找的项目可能是：{CurrentSelectedProjectName}");
                }
            }
        }

        class CharFragment
        {
            public int startIndex;
            public int endIndex;
            public bool shouldHighlight;

            public CharFragment(int startIndex, int endIndex, bool shouldHighlight)
            {
                this.startIndex = startIndex;
                this.endIndex = endIndex;
                this.shouldHighlight = shouldHighlight;
            }
        }

        private async void SetTextToRichTextBox(List<ProjectInfo> projects, List<string> keywords = null, string prefix = "")
        {
            ProjectListRichTextBox.Document.Blocks.Clear();
            ProjectListRichTextBox.Document.PageWidth = 1920;
            ProjectListRichTextBox.Document.LineHeight = 17;

            if (prefix != "")
            {
                Paragraph prefixPara = new Paragraph();
                prefixPara.Inlines.Add(new Run(prefix));
                ProjectListRichTextBox.Document.Blocks.Add(prefixPara);
            }

            Paragraph para = new Paragraph();
            if (projects?.Count > 0)
            {
                foreach (ProjectInfo project in projects.Take(64))
                {
                    string projectName = project.Name;
                    if (keywords == null)
                    {
                        para.Inlines.Add(new Run(projectName));
                    }
                    else
                    {
                        bool[] matched = new bool[projectName.Length];
                        foreach (var keyword in keywords)
                        {
                            int startIndex = 0;
                            do
                            {
                                startIndex = projectName.IndexOf(keyword, startIndex, StringComparison.OrdinalIgnoreCase);
                                if (startIndex >= 0)
                                {
                                    for (int i = startIndex; i < startIndex + keyword.Length; ++i)
                                    {
                                        matched[i] = true;
                                    }
                                    startIndex += keyword.Length;
                                }
                            } while (startIndex >= 0);
                        }

                        List<CharFragment> fragments = new List<CharFragment>();
                        int head = 0;
                        for (int i = 1; i < projectName.Length; ++i)
                        {
                            if (matched[i] != matched[head])
                            {
                                fragments.Add(new CharFragment(head, i - 1, matched[head]));
                                head = i;
                            }
                        }
                        fragments.Add(new CharFragment(head, projectName.Length - 1, matched[head]));
                        foreach (CharFragment f in fragments)
                        {
                            if (f.shouldHighlight)
                            {
                                para.Inlines.Add(new Run(projectName.Substring(f.startIndex, f.endIndex - f.startIndex + 1))
                                {
                                    //FontWeight = FontWeights.Bold,
                                    Background = System.Windows.Media.Brushes.Yellow
                                });
                            }
                            else
                            {
                                para.Inlines.Add(new Run(projectName.Substring(f.startIndex, f.endIndex - f.startIndex + 1)));
                            }
                        }
                    }

                    para.Inlines.Add(new Run($" ({project.Path})"));
                    para.Inlines.Add(new LineBreak());
                }
                if (projects.Count > 64)
                {
                    para.Inlines.Add(new Run($"..."));
                }
            }

            ProjectListRichTextBox.Document.Blocks.Add(para);
        }

        private async void ProjectNameTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (CurrentSelectedProjectName != null)
                {
                    _ = await SetAsStartupProject();
                    _ = await NavigateTo();
                }
                e.Handled = true;
            }
        }

        private void EllyeRun_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Run run)
            {
                run.Foreground = new SolidColorBrush(Colors.LightCoral);
            }
        }

        private void EllyeRun_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Run run)
            {
                run.Foreground = new SolidColorBrush(Colors.Black);
            }
        }
    }
}
