using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json;

namespace MURDOC_2024.Services
{
    internal sealed class PythonModelService : IPythonService, IDisposable
    {
        private double _currentSensitivity = 1.5;  // Default (no change)
        private double _currentBias = 0.0;         // Default (no change)

        // NOTE: You must either define pythonHome as a class field 
        // or repeat the path string here to use it in the constructor.
        private readonly string _pythonHome = @"C:\Users\hogue\AppData\Local\Python\Python39";

        public PythonModelService()
        {
            // Add Python Home to the System PATH so Process.Start("python.exe")
            // resolves to the correct Python 3.9 interpreter on all threads.
            if (Directory.Exists(_pythonHome))
            {
                string path = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);

                // Only add if it's not already present (case-insensitive check)
                if (!path.ToLower().Contains(_pythonHome.ToLower()))
                {
                    Environment.SetEnvironmentVariable("PATH", _pythonHome + Path.PathSeparator + path, EnvironmentVariableTarget.Process);
                    Console.WriteLine($"[DEBUG] Added {_pythonHome} to PATH for background threads.");
                }
            }
        }

        /// <summary>
        /// Runs the Python model on a background thread to prevent UI blocking.
        /// </summary>
        public Task<string> RunIAIModelsBypassAsync(string imagePath)
        {
            // Always write current parameters before running
            SetDetectionParameters(_currentSensitivity, _currentBias);

            // 1. Get Python path (assuming it's in the PATH now)
            string pythonExe = "python.exe";

            // 2. Prepare arguments to call your script and function
            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Model\IAI_Decision_Hierarchy.py");

            // Check if the script exists
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"Python script not found at: {scriptPath}");
            }

            // The Python script will receive this as sys.argv[1]
            // CRITICAL: Define the target output directory as the C# Debug folder
            string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results");

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // 2. Prepare arguments: Script path, image path, AND OUTPUT DIR
            string arguments = $"\"{scriptPath}\" \"{imagePath}\"";

            Console.WriteLine($"[PROCESS] Executing: {pythonExe} {arguments}");

            return RunPythonScriptAsync(pythonExe, arguments);
        }

        /// <summary>
        /// Sets detection parameters for MICA controls
        /// </summary>
        public void SetDetectionParameters(double sensitivity, double bias)
        {
            _currentSensitivity = sensitivity;
            _currentBias = bias;

            // Write to the location Python expects
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "mica_params.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)); // Ensure directory exists

            var config = new
            {
                sensitivity = sensitivity,
                bias = bias,
                timestamp = DateTime.Now
            };
            File.WriteAllText(configPath, JsonConvert.SerializeObject(config));
        }

        /// <summary>
        /// Runs model with MICA parameters
        /// </summary>
        public Task<string> RunIAIModelsWithMICAAsync(string imagePath)
        {
            // 1. Get Python path (assuming it's in the PATH now)
            string pythonExe = "python.exe";

            // 2. Prepare script path
            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\Model\IAI_Decision_Hierarchy.py");

            // Check if the script exists
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"Python script not found at: {scriptPath}");
            }

            // Define the target output directory as the C# Debug folder
            string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results");

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // 3. Pass parameters as command line arguments INCLUDING MICA parameters
            string arguments = $"\"{scriptPath}\" \"{imagePath}\" --sensitivity {_currentSensitivity} --bias {_currentBias}";

            Console.WriteLine($"[PROCESS] Executing: {pythonExe} {arguments}");

            return RunPythonScriptAsync(pythonExe, arguments);
        }

        // **NOTE: The original synchronous 'public string RunIAIModels(string imagePath)'
        // has been removed/deleted from this class.**

        /// <summary>
        /// Shared process runner for both bypass and MICA model paths.
        ///
        /// Reads stdout and stderr concurrently on separate tasks before waiting for
        /// exit — avoiding the OS pipe-buffer deadlock that occurs when WaitForExit()
        /// is called before ReadToEnd() on a process with heavy output.
        ///
        /// On non-zero exit, throws a PythonExecutionException that carries the full
        /// stderr traceback so callers can surface a meaningful error to the user.
        /// On success, any stderr content is logged as a warning but not thrown.
        /// </summary>
        private Task<string> RunPythonScriptAsync(string pythonExe, string arguments)
        {
            return Task.Run(() =>
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = arguments,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    // Read both streams concurrently BEFORE WaitForExit to prevent
                    // OS pipe-buffer deadlock on large model output.
                    var stdoutTask = System.Threading.Tasks.Task.Run(
                        () => process.StandardOutput.ReadToEnd());
                    var stderrTask = System.Threading.Tasks.Task.Run(
                        () => process.StandardError.ReadToEnd());

                    process.WaitForExit();

                    string output = stdoutTask.Result;
                    string errors = stderrTask.Result;

                    if (process.ExitCode != 0)
                    {
                        // Build a clean traceback message — strip blank lines from stderr
                        // and prepend the exit code so the caller can show something useful.
                        string traceback = string.IsNullOrWhiteSpace(errors)
                            ? "(no stderr captured)"
                            : errors.Trim();

                        Console.WriteLine($"[PYTHON CRASH] Exit {process.ExitCode}\n{traceback}");

                        throw new Exception(
                            $"Python exited with code {process.ExitCode}.\n\n{traceback}");
                    }

                    if (!string.IsNullOrEmpty(errors))
                        Console.WriteLine($"[PYTHON WARNINGS] {errors}");

                    return output.Trim();
                }
            });
        }

        /// <summary>Releases resources. No-op since all Python execution is via subprocess.</summary>
        public void Dispose()
        {
            // All Python execution uses Process.Start, no embedded engine to shut down
        }
    }
}