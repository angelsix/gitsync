using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AngelSix.GitSync
{
    public class Settings
    {
        public string Directory { get; set; }

        public bool Fetch { get; set; }

        public bool Force { get; set; }

        public OperationMode Mode { get; set; } = OperationMode.None;
    }

    public enum OperationMode
    {
        None = 0,
        Check = 1,
        Pull = 2,
        Push = 3,
        BranchDisplay = 4,
        BranchDisplayAll = 5,
        Clean = 6
    }

    public class Program
    {
        //   NOTE TO ALL WHO PASS HERE
        // 
        //   Please ignore the total mess here for now. This was a quick one-day project
        //   I will clean this up in the near future and expand upon this
        //
        //    - AngelSix
        //   

        static Process process = new Process();

        private static string SettingsFilename = "settings.json";

        private static string UserSettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitSync", SettingsFilename);

        public static void Main(string[] args)
        {
            // Get executing directory
            var currentDirectory = Environment.CurrentDirectory;

            // If this is being called from its shortcut this will match the assembly location
            var assemblyDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location);

            Console.WriteLine("Current directory: " + currentDirectory);
            Console.WriteLine("Install directory: " + assemblyDirectory);

            // If directories don't match, then override settings directory with current directory
            var overrideDirectoryWithCurrent = !string.Equals(currentDirectory, assemblyDirectory, StringComparison.InvariantCultureIgnoreCase);

            // Detect if we have no settings file locally or in the users folder (C:\users\???\.gitsync\settings.json)
            if (!File.Exists(UserSettingsPath))
            {
                Console.WriteLine("No settings file exists.");
                Console.WriteLine("Please enter the folder where all your git repos are.");
                Console.WriteLine("For example `D:\\git`...");
                Console.Write("Directory: ");

                // Read in desired top-level directory
                // Then normalize slashes and escape them for json
                var directory = Console.ReadLine().Replace("/", "\\").Replace("\\", "\\\\");

                // Ensure folder
                var folder = Path.GetDirectoryName(UserSettingsPath);
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                // Write file
                File.WriteAllText(UserSettingsPath, "{" +
                    "  \"Directory\": \"" + directory + "\" " +
                    " }");

                // Let user know
                Console.WriteLine($"Settings file created in {UserSettingsPath} with main git directory set to {directory}");
                Console.WriteLine("");
            }

            while (true)
            {
                #region Initialize

                var settings = new Settings();

                // Pull in settings file
                if (File.Exists(UserSettingsPath))
                {
                    try
                    {
                        settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(UserSettingsPath));

                        Console.WriteLine("Loaded settings file");

                        // If launched from a specific folder...
                        if (overrideDirectoryWithCurrent)
                        {
                            // Use that over a settings file
                            settings.Directory = currentDirectory;

                            Console.WriteLine("Preferring current directory over settings file directory");
                        }
                    }
                    catch
                    {
                        Console.WriteLine($"Bad settings file {UserSettingsPath}. Deleting...");
                        File.Delete(UserSettingsPath);
                    }
                }

                var pendingChanges = new List<string>();
                var pendingPushes = new List<string>();
                var mergeChanges = new List<string>();
                var branches = new List<Tuple<string, string>>();
                var allBranches = new List<Tuple<string, List<string>>>();

                ExtractArguments(args, settings);

                #endregion

                #region Simple Command

                // Ask for initial simple command
                Console.Write(
                    "What command would you like? \n\n" +
                    "  b - branch \n" + 
                    "  c - check \n" + 
                    "  C - clean \n" +
                    "  p - pull \n" +
                    "  P - push \n" +
                    "  help - for more information \n\n" +
                    "Press enter to exit...\n");
                var command = Console.ReadLine();
                var commandUnderstood = false;

                if (command == "branch" || command == "b")
                {
                    settings.Mode = OperationMode.BranchDisplay;
                    commandUnderstood = true;
                }
                else if (command == "check" || command == "c")
                {
                    settings.Mode = OperationMode.Check;
                    commandUnderstood = true;
                }
                else if (command == "clean" || command == "C")
                {
                    settings.Mode = OperationMode.Clean;
                    commandUnderstood = true;
                }
                else if (command == "pull" || command == "p")
                {
                    settings.Mode = OperationMode.Pull;
                    commandUnderstood = true;
                }
                else if (command == "push" || command == "P")
                {
                    settings.Mode = OperationMode.Push;
                    commandUnderstood = true;
                }

                #endregion

                #region Help

                if (!commandUnderstood && (command == "help"))
                {
                    Console.WriteLine("");
                    Console.WriteLine("-d [folder]".PadRight(13) + "Specify parent git folder");
                    Console.WriteLine("");
                    Console.WriteLine("MODE");
                    Console.WriteLine("-c".PadRight(13) + "Check mode (show which repo's have pending changes to commit)");
                    Console.WriteLine("-p".PadRight(13) + "Pull mode (pull's all repo's, if there are no local changes)");
                    Console.WriteLine("-P".PadRight(13) + "Push mode (pull's then pushes all repo's, if there are no local changes)");
                    Console.WriteLine("-b".PadRight(13) + "Branch display mode (Displays what branch all repo's are on, * = local changes)");
                    Console.WriteLine("");
                    Console.WriteLine("OPTIONS");
                    Console.WriteLine("");
                    Console.WriteLine("-f".PadRight(13) + "Fetch (Fetches the remote origin prior to the mode running)");
                    Console.WriteLine("-F".PadRight(13) + "Force (Pushes and/or pulls regardless of pending changes)");
                    Console.WriteLine("");
                    Console.WriteLine("EXTRA");
                    Console.WriteLine("");
                    Console.WriteLine("-C".PadRight(13) + "Cleans all bin/obj and .vs folders");
                    Console.WriteLine("");
                    Console.WriteLine("EXAMPLES");
                    Console.WriteLine("-d . -c".PadRight(13) + "List all repo's that have local changes");
                    Console.WriteLine("-d . -c -f".PadRight(13) + "List all repo's that have local changes (perform fetch first)");
                    Console.WriteLine("-d . -b".PadRight(13) + "List the currently tracked branches of all repo's");
                    Console.WriteLine("-d . -p".PadRight(13) + "Pulls all local changes if no local changes or remote conflicts");
                    Console.WriteLine("-d . -p -F".PadRight(13) + "Tries to pull all local changes regardless of conflicts or local changes");
                    Console.WriteLine("-d . -P".PadRight(13) + "Pushes all local changes if no local changes or remote conflicts");
                    Console.WriteLine("-d . -P -F".PadRight(13) + "Tries to push all local changes regardless of conflicts or local changes");
                    Console.WriteLine("");

                    continue;
                }

                #endregion

                #region Exit

                // If the user did not enter anything, then exit
                if (args == null || args.Length == 0)
                    return;

                #endregion

                #region Check

                if (settings.Mode == OperationMode.None)
                {
                    Console.WriteLine("No mode specified");
                    continue;
                }

                if (string.IsNullOrEmpty(settings.Directory))
                {
                    Console.WriteLine($"Please specify a folder with -d folder");
                    continue;
                }

                #endregion

                // If we are not fetching, but our mode is pull or push...
                if (!settings.Fetch && (settings.Mode == OperationMode.Pull || settings.Mode == OperationMode.Push))
                    // Force a fetch anyway
                    settings.Fetch = true;

                Console.WriteLine($"{settings.Mode} mode...");

                if (settings.Mode == OperationMode.Clean)
                {
                    // Find all VS folders
                    var folders = FindVSFolders(settings.Directory).OrderBy(f => f).ToList();

                    Console.WriteLine("");

                    // Try and delete them
                    folders.ForEach(folder =>
                    {
                        try
                        {
                            Console.WriteLine($"Deleting {folder}...");

                            Directory.Delete(folder, true);
                        }
                        catch
                        {
                            Console.WriteLine("Failed to delete folder");
                        }
                    });
                }
                else
                {
                    // Find all folders that have a .git folder in them
                    var gitRepos = FindGitRepos(settings.Directory);

                    // For each repo
                    gitRepos.ForEach(repo =>
                    {
                        if (settings.Mode == OperationMode.BranchDisplay)
                        {
                            // FETCH
                            var changes = !GitCheck(repo, settings.Mode, settings.Fetch, pendingChanges, pendingPushes);

                            branches.Add(new Tuple<string, string>(repo, RunProcess("git", "rev-parse --abbrev-ref HEAD", repo).Replace("\r", "").Replace("\n", "") + (changes ? " *" : "")));
                        }
                        else if (settings.Mode == OperationMode.BranchDisplayAll)
                        {
                            // Get if we have any changes on current branch
                            var changes = !GitCheck(repo, settings.Mode, settings.Fetch, pendingChanges, pendingPushes);

                            // Get current branch
                            var currentBranch = RunProcess("git", "rev-parse --abbrev-ref HEAD", repo).Replace("\r", "").Replace("\n", "");

                            var combinedBranches = new List<string>();

                            // Get all local branches (removing * and trim spaces from start and end)
                            var localBranches = RunProcess("git", "branch", repo).Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Replace("*", "").Trim()).ToList();

                            // Remote branches (removing * and trim spaces from start and end)
                            var remoteBranches = RunProcess("git", "branch -r", repo).Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Replace("*", "").Trim()).ToList();

                            // String origin/ from the start of remote branches so they match the local names
                            if (remoteBranches.Count > 0)
                                remoteBranches = remoteBranches.Select(remote =>
                                {
                                    if (!remote.StartsWith("origin/"))
                                        return remote;

                                    return remote.Substring("origin/".Length);
                                }).ToList();

                            // Combine both branches, adding (L) for local only, (LR) for local and remote
                            combinedBranches = localBranches.Select(local => (local == currentBranch ? " * " : "") + (remoteBranches.Any(remote => remote == local) ? (local + " (LR)") : (local + " (L)"))).ToList();

                            // Now filter the ones that are only in the remote
                            remoteBranches = remoteBranches.Where(remote => !localBranches.Any(local => local == remote)).Select(remote => remote + " (R)").ToList();

                            // And if there are any, add those
                            if (remoteBranches.Count > 0)
                                combinedBranches.AddRange(remoteBranches);

                            // Order
                            combinedBranches.Sort();

                            // Add them all
                            allBranches.Add(new Tuple<string, List<string>>(repo + (changes ? " *" : ""), combinedBranches));
                        }
                        else
                        {
                            // FETCH
                            if (!GitCheck(repo, settings.Mode, settings.Fetch, pendingChanges, pendingPushes) && !settings.Force)
                                return;

                            // No changes, so if we want to pull, do that next
                            if (settings.Mode != OperationMode.Pull && settings.Mode != OperationMode.Push)
                                return;

                            // PULL
                            if (!GitPull(repo, settings.Mode, mergeChanges) && !settings.Force)
                                return;

                            GitPush(repo, settings.Mode);
                        }
                    });

                    // If we have any pending changes, output
                    Console.WriteLine("");

                    if (settings.Mode == OperationMode.BranchDisplay)
                    {
                        if (branches.Count > 0)
                        {
                            var maxLength = branches.Max(f => f.Item1.Length) + 3;

                            branches.ForEach(branch => Console.WriteLine(branch.Item1.PadRight(maxLength) + branch.Item2));
                        }
                    }
                    else if (settings.Mode == OperationMode.BranchDisplayAll)
                    {
                        // Legend
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("Green repo name = pending changes");
                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine("(L) = Branches only on local");
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("(R) = Branches only on remote");
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("* = Current active branch");
                        Console.ResetColor();
                        Console.WriteLine();

                        if (allBranches.Count > 0)
                        {
                            allBranches.ForEach(branch =>
                            {
                                // Color changes green
                                if (branch.Item1.Contains("*"))
                                    Console.ForegroundColor = ConsoleColor.Green;

                                // Output repo name
                                Console.WriteLine(branch.Item1.Replace("*", ""));

                                Console.ResetColor();

                                // Now output all branches
                                branch.Item2.ForEach(b =>
                                    {
                                        // Color remote only branches red
                                        if (b.Contains("(R)"))
                                            Console.ForegroundColor = ConsoleColor.Red;
                                        // Color local only branches green
                                        if (b.Contains("(L)"))
                                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                                        // Color current branch blue
                                        if (b.Contains("*"))
                                            Console.ForegroundColor = ConsoleColor.Cyan;

                                        // Output branch
                                        Console.WriteLine((b.Contains("*") ? "" : "   ") + b);

                                        // Reset color
                                        Console.ResetColor();
                                    });

                                Console.WriteLine(string.Empty);
                            });
                        }
                    }
                    else
                    {
                        if (pendingChanges.Count == 0)
                            Console.WriteLine("No pending changes");
                        else
                        {
                            Console.WriteLine("Pending changes...");
                            pendingChanges.ForEach(pending => Console.WriteLine(pending));
                        }
                        Console.WriteLine("");

                        if (pendingPushes.Count == 0)
                            Console.WriteLine("No pending pushes");
                        else
                        {
                            Console.WriteLine("Pending pushes...");
                            pendingPushes.ForEach(pending => Console.WriteLine(pending));
                        }
                        Console.WriteLine("");

                        // If we have any merge conflicts, output
                        if (mergeChanges.Count == 0)
                            Console.WriteLine("No merge conflicts");
                        else
                        {
                            Console.WriteLine("Merge conflicts...");
                            mergeChanges.ForEach(mergeConflict => Console.WriteLine(mergeConflict));
                        }
                        Console.WriteLine("");
                    }
                }

                // Let the user know that the task has finished
                Console.WriteLine("\nFinished task.\n");
            }
        }

        private static bool DoMore()
        {
            Console.WriteLine("Done. Press y to do another command, or any other key to close...");
            var more = Console.ReadLine().ToLower() == "y";
            Console.WriteLine("");

            return more;
        }

        private static void ExtractArguments(string[] args, Settings settings)
        {
            // Load mode and directory
            if (args?.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-c")
                        settings.Mode = OperationMode.Check;
                    else if (args[i] == "-p")
                        settings.Mode = OperationMode.Pull;
                    else if (args[i] == "-P")
                        settings.Mode = OperationMode.Push;
                    else if (args[i] == "-b")
                        settings.Mode = OperationMode.BranchDisplay;
                    else if (args[i] == "-B")
                        settings.Mode = OperationMode.BranchDisplayAll;
                    else if (args[i] == "-C")
                        settings.Mode = OperationMode.Clean;
                    else if (args[i] == "-f")
                        settings.Fetch = true;
                    else if (args[i] == "-F")
                        settings.Force = true;
                    else if (args[i] == "-d" && i + 1 < args.Length)
                        settings.Directory = args[++i];
                }
            }
        }

        private static void GitPush(string repo, OperationMode mode)
        {
            // 
            //    GIT PUSH
            //

            // Pull worked, so push if needed
            if (mode != OperationMode.Push)
                return;

            // Check if we have something to push
            var gitResult = RunProcess("git", "status -u", repo);

            if (gitResult != null && gitResult.Contains("Your branch is ahead of"))
            {
                Console.WriteLine($"Pushing {repo}...");

                gitResult = RunProcess("git", "push", repo);

                // Write the git results of a push
                Console.WriteLine(gitResult);
            }
            else
            {
                Console.WriteLine($"Nothing to push for {repo}");
            }
        }

        private static bool GitPull(string repo, OperationMode mode, List<string> mergeChanges)
        {
            // 
            //    GIT PULL
            //

            Console.WriteLine($"Pulling {repo}...");
            var gitResult = RunProcess("git", "pull", repo);

            // See if it failed
            if (gitResult != null && gitResult.Contains("Automatic merge failed"))
            {
                Console.WriteLine("*** Merge failed, skipping ***");
                mergeChanges.Add(repo);
                return false;
            }

            return true;
        }

        private static bool GitCheck(string repo, OperationMode mode, bool fetch, List<string> pendingChanges, List<string> pendingPushes)
        {
            // 
            //    GIT FETCH
            //

            if (fetch || mode == OperationMode.Pull || mode == OperationMode.Push)
            {
                Console.WriteLine($"Fetching {repo}...");
                RunProcess("git", "fetch", repo);
            }

            // Check git status, and look for the string "nothing to commit"
            var gitResult = RunProcess("git", "status -u", repo);

            if (gitResult != null && gitResult.Contains("Your branch is ahead"))
            {
                Console.WriteLine("*** Pending push ***");
                Console.WriteLine(gitResult);

                pendingPushes.Add(repo);
            }

            if (gitResult != null && !gitResult.Contains("nothing to commit"))
            {
                // If this repo can be fast forwarded and we are pulling/pushing, let it carry on
                if (gitResult.Contains("can be fast-forwarded") && (mode == OperationMode.Pull || mode == OperationMode.Push))
                {
                    if (mode != OperationMode.BranchDisplay)
                    {
                        if (mode == OperationMode.Check)
                            Console.WriteLine($"{repo}");

                        Console.WriteLine("*** Pending changes, fast-forwarding ***");
                    }
                }
                else
                {
                    if (mode != OperationMode.BranchDisplay)
                    {
                        if (mode == OperationMode.Check)
                            Console.WriteLine($"{repo}");

                        Console.WriteLine("*** Pending changes, skipping ***");
                        Console.WriteLine(gitResult);
                    }

                    pendingChanges.Add(repo);
                    return false;
                }
            }

            return true;
        }

        private static string RunProcess(string filename, string argument, string workingDirectory)
        {
            var p = new Process();
            p.StartInfo.FileName = filename;
            p.StartInfo.Arguments = argument;
            p.StartInfo.WorkingDirectory = workingDirectory;
            p.StartInfo.RedirectStandardOutput = true;
            //p.StartInfo.RedirectStandardError = true;
            p.Start();

            return p.StandardOutput.ReadToEnd();
        }

        private static List<string> FindVSFolders(string folder)
        {
            var vsFolders = new List<string>(Directory.GetDirectories(folder, ".vs", SearchOption.AllDirectories));
            var binFolders = new List<string>(Directory.GetDirectories(folder, "bin", SearchOption.AllDirectories));
            var objFolders = new List<string>(Directory.GetDirectories(folder, "obj", SearchOption.AllDirectories));

            if (binFolders.Count > 0)
                vsFolders.AddRange(binFolders);
            if (objFolders.Count > 0)
                vsFolders.AddRange(objFolders);

            // Filter out folders if their parent doesn't contain a .sln, .xproj, .csproj or project.json file
            vsFolders = vsFolders.Where(f =>
            {
                var parent = Path.GetDirectoryName(f);

                if (File.Exists(Path.Combine(parent, "project.json")))
                    return true;

                if (Directory.GetFiles(parent, "*.csproj", SearchOption.TopDirectoryOnly).Length > 0)
                    return true;

                if (Directory.GetFiles(parent, "*.xproj", SearchOption.TopDirectoryOnly).Length > 0)
                    return true;

                var a = Directory.GetFiles(parent, "*.sln", SearchOption.TopDirectoryOnly);

                if (Directory.GetFiles(parent, "*.sln", SearchOption.TopDirectoryOnly).Length > 0)
                    return true;

                Console.WriteLine($"Ignoring {f} as no VS folder structure detected");

                return false;

            }).ToList();

            return vsFolders;
        }

        private static List<string> FindGitRepos(string folder)
        {
            var repos = new List<string>(Directory.GetDirectories(folder, ".git", SearchOption.AllDirectories));
            return repos.Select(f => Path.GetDirectoryName(f)).ToList();
        }
    }
}