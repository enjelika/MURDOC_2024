using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Python.Runtime;

namespace MURDOC_2024.Services
{
    /// <summary>
    /// Thread-safe Python model service with proper async support
    /// </summary>
    internal sealed class PythonModelService : IDisposable
    {
        private bool _isInitialized = false;
        private readonly object _initLock = new object();
        private readonly SemaphoreSlim _pythonSemaphore = new SemaphoreSlim(1, 1);

        public PythonModelService()
        {
            InitializePythonRuntime();
        }

        /// <summary>
        /// Initializes Python Engine safely for WPF and Python 3.9
        /// </summary>
        private void InitializePythonRuntime()
        {
            lock (_initLock)
            {
                if (_isInitialized)
                    return;

                try
                {
                    Console.WriteLine("Starting Python runtime initialization...");

                    // Python installation paths
                    string pythonHome = @"C:\Users\hogue\AppData\Local\Python\Python39";
                    string pythonDll = Path.Combine(pythonHome, "python39.dll");

                    if (!File.Exists(pythonDll))
                    {
                        // Try alternative common locations
                        string[] alternativePaths = {
                            @"C:\Python39",
                            @"C:\Program Files\Python39",
                            @"C:\Program Files (x86)\Python39",
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python39")
                        };

                        foreach (var altPath in alternativePaths)
                        {
                            var altDll = Path.Combine(altPath, "python39.dll");
                            if (File.Exists(altDll))
                            {
                                pythonHome = altPath;
                                pythonDll = altDll;
                                break;
                            }
                        }

                        if (!File.Exists(pythonDll))
                            throw new FileNotFoundException($"Python 3.9 DLL not found. Please ensure Python 3.9 is installed.");
                    }

                    Console.WriteLine($"Python Home: {pythonHome}");
                    Console.WriteLine($"Python DLL: {pythonDll}");

                    // Required environment variables BEFORE PythonEngine.Initialize()
                    Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", pythonDll);
                    Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome);

                    string pythonPath = string.Join(";",
                        Path.Combine(pythonHome, "Lib"),
                        Path.Combine(pythonHome, "Lib", "site-packages"),
                        Path.Combine(pythonHome, "Scripts")
                    );

                    Environment.SetEnvironmentVariable("PYTHONPATH", pythonPath);

                    // Initialize PythonEngine
                    PythonEngine.Initialize();
                    _isInitialized = true;

                    Console.WriteLine("Python runtime initialized successfully.");

                    // Validate Python with a simple import
                    using (Py.GIL())
                    {
                        dynamic sys = Py.Import("sys");
                        Console.WriteLine($"Python version: {sys.version}");

                        // Add common paths
                        Console.WriteLine("Python sys.path:");
                        foreach (var p in sys.path)
                            Console.WriteLine($"  {p}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error initializing Python engine: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    _isInitialized = false;
                    throw new InvalidOperationException("Failed to initialize Python runtime. Please ensure Python 3.9 is installed.", ex);
                }
            }
        }

        /// <summary>
        /// Runs IAI models asynchronously
        /// </summary>
        public async Task<string> RunIAIModelsAsync(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                throw new ArgumentNullException(nameof(imagePath));

            if (!File.Exists(imagePath))
                throw new FileNotFoundException($"Image file not found: {imagePath}");

            // Use semaphore to ensure only one Python operation at a time
            await _pythonSemaphore.WaitAsync();
            try
            {
                return await Task.Run(() => RunIAIModelsInternal(imagePath));
            }
            finally
            {
                _pythonSemaphore.Release();
            }
        }

        /// <summary>
        /// Synchronous version for backward compatibility
        /// </summary>
        public string RunIAIModels(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                throw new ArgumentNullException(nameof(imagePath));

            if (!File.Exists(imagePath))
                throw new FileNotFoundException($"Image file not found: {imagePath}");

            _pythonSemaphore.Wait();
            try
            {
                return RunIAIModelsInternal(imagePath);
            }
            finally
            {
                _pythonSemaphore.Release();
            }
        }

        /// <summary>
        /// Internal method that actually runs the Python code
        /// </summary>
        private string RunIAIModelsInternal(string imagePath)
        {
            if (!_isInitialized)
            {
                InitializePythonRuntime();
            }

            using (Py.GIL())
            {
                try
                {
                    dynamic sys = Py.Import("sys");
                    dynamic os = Py.Import("os");

                    // Find the Model directory
                    string projectRoot = FindProjectRoot();
                    string scriptDir = Path.Combine(projectRoot, "Model");

                    if (!Directory.Exists(scriptDir))
                    {
                        // Try alternative locations
                        scriptDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Model");
                        if (!Directory.Exists(scriptDir))
                        {
                            throw new DirectoryNotFoundException($"Model directory not found. Expected at: {scriptDir}");
                        }
                    }

                    Console.WriteLine($"[Python] Adding script directory: {scriptDir}");

                    // Clear and add the script directory to sys.path
                    bool pathExists = false;
                    foreach (var p in sys.path)
                    {
                        if (p.ToString() == scriptDir)
                        {
                            pathExists = true;
                            break;
                        }
                    }

                    if (!pathExists)
                    {
                        sys.path.insert(0, scriptDir);
                    }

                    // List files in the directory for debugging
                    Console.WriteLine($"[Python] Files in {scriptDir}:");
                    foreach (string f in Directory.GetFiles(scriptDir, "*.py"))
                    {
                        Console.WriteLine($"  - {Path.GetFileName(f)}");
                    }

                    // Import the script
                    dynamic script = null;
                    try
                    {
                        script = Py.Import("IAI_Decision_Hierarchy");
                    }
                    catch (PythonException pex)
                    {
                        Console.WriteLine($"[Python] Failed to import IAI_Decision_Hierarchy: {pex.Message}");
                        // Try alternative import method
                        dynamic importlib = Py.Import("importlib.util");
                        string scriptPath = Path.Combine(scriptDir, "IAI_Decision_Hierarchy.py");
                        if (File.Exists(scriptPath))
                        {
                            dynamic spec = importlib.spec_from_file_location("IAI_Decision_Hierarchy", scriptPath);
                            script = importlib.module_from_spec(spec);
                            spec.loader.exec_module(script);
                        }
                        else
                        {
                            throw new FileNotFoundException($"Script not found: {scriptPath}");
                        }
                    }

                    Console.WriteLine($"[Python] Running iaiDecision({imagePath})");

                    // Call the function
                    string result = script.iaiDecision(imagePath).ToString();

                    Console.WriteLine($"[Python] Result: {result}");

                    return result;
                }
                catch (PythonException pex)
                {
                    Console.WriteLine($"[Python] Error: {pex.Message}");
                    Console.WriteLine($"[Python] Stack trace: {pex.StackTrace}");
                    throw new InvalidOperationException($"Python execution error: {pex.Message}", pex);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Python] Unexpected error: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Finds the project root directory
        /// </summary>
        private string FindProjectRoot()
        {
            // Try to find the project root by looking for key markers
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;

            // If running from bin\Debug or bin\Release
            DirectoryInfo dir = new DirectoryInfo(currentDir);
            while (dir != null)
            {
                // Look for project file or Model directory
                if (Directory.Exists(Path.Combine(dir.FullName, "Model")) ||
                    File.Exists(Path.Combine(dir.FullName, "MURDOC_2024.csproj")) ||
                    File.Exists(Path.Combine(dir.FullName, "MURDOC_2024.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            // Fallback to the original method
            return Path.GetFullPath(Path.Combine(currentDir, @"..\..\.."));
        }

        public void Dispose()
        {
            lock (_initLock)
            {
                if (_isInitialized)
                {
                    try
                    {
                        PythonEngine.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error shutting down Python engine: {ex.Message}");
                    }
                    finally
                    {
                        _isInitialized = false;
                    }
                }
            }

            _pythonSemaphore?.Dispose();
        }
    }
}