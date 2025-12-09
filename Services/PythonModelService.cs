using System;
using System.IO;
using System.Threading.Tasks;
using Python.Runtime;
using System.Diagnostics;

namespace MURDOC_2024.Services
{
    internal sealed class PythonModelService : IDisposable
    {
        private bool _isInitialized = false;

        // NOTE: You must either define pythonHome as a class field 
        // or repeat the path string here to use it in the constructor.
        private readonly string _pythonHome = @"C:\Users\hogue\AppData\Local\Python\Python39";

        public PythonModelService()
        {
            InitializePythonRuntime();

            // -------------------------------------------------------------
            // **CRITICAL FIX: Add Python Home to the System PATH for all threads**
            // -------------------------------------------------------------
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
            // -------------------------------------------------------------
        }

        /// <summary>
        /// Initializes Python Engine safely for WPF and Python 3.9
        /// </summary>
        private void InitializePythonRuntime()
        {
            if (_isInitialized)
                return;

            try
            {
                Console.WriteLine("Starting Python runtime initialization...");

                // Python installation paths (use your actual paths)
                string pythonHome = @"C:\Users\hogue\AppData\Local\Python\Python39";
                string pythonDll = Path.Combine(pythonHome, "python39.dll");

                if (!File.Exists(pythonDll))
                    throw new FileNotFoundException($"Python DLL not found at {pythonDll}");

                Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", pythonDll);
                Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome);

                string pythonPath = string.Join(";",
                    Path.Combine(pythonHome, "Lib"),
                    Path.Combine(pythonHome, "Lib", "site-packages")
                );

                Environment.SetEnvironmentVariable("PYTHONPATH", pythonPath);

                // Initialize PythonEngine on UI thread (C# side)
                PythonEngine.Initialize();
                _isInitialized = true;

                Console.WriteLine("Python runtime initialized successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error initializing Python engine: " + ex);
                throw;
            }
        }

        /// <summary>
        /// Runs the Python model on a background thread to prevent UI blocking.
        /// </summary>
        public Task<string> RunIAIModelsBypassAsync(string imagePath)
        {
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

            return Task.Run(() =>
            {
                ProcessStartInfo start = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true, // Capture output for result
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(start))
                {
                    // Wait for the process to exit
                    process.WaitForExit();

                    // Read output (result) and errors
                    string output = process.StandardOutput.ReadToEnd();
                    string errors = process.StandardError.ReadToEnd();

                    if (process.ExitCode != 0) // Process actually crashed
                    {
                        string fullError = errors + "\nOutput: " + output;
                        Console.WriteLine($"[EXTERNAL PYTHON CRASH] {fullError}");
                        throw new Exception($"Python process crashed with exit code {process.ExitCode}. Errors:\n{fullError}");
                    }

                    // --------------------------------------------------------------------------
                    // CRITICAL FIX: Only throw if the process failed AND the output is empty.
                    // If exit code is 0 (success) and we have standard output, the warnings can be ignored.
                    // --------------------------------------------------------------------------
                    if (!string.IsNullOrEmpty(errors))
                    {
                        // Log the warnings, but do NOT throw since ExitCode was 0.
                        Console.WriteLine($"[EXTERNAL PYTHON WARNINGS/MESSAGES] Errors (Ignored): {errors}");
                    }

                    // NOTE: Your Python script must be modified to print ONLY the final result string to stdout.
                    return output.Trim();
                }
            });
        }

        // **NOTE: The original synchronous 'public string RunIAIModels(string imagePath)' 
        // has been removed/deleted from this class.**

        public void Dispose()
        {
            if (_isInitialized)
            {
                Console.WriteLine("[DEBUG] Shutting down Python Engine."); // Add log
                PythonEngine.Shutdown();
                _isInitialized = false;
            }
        }
    }
}