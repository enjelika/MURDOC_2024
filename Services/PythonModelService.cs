using System;
using System.IO;
using Python.Runtime;

namespace MURDOC_2024.Services
{
    internal sealed class PythonModelService : IDisposable
    {
        private bool _isInitialized = false;

        public PythonModelService()
        {
            InitializePythonRuntime();
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

                // Python installation paths
                string pythonHome = @"C:\Users\hogue\AppData\Local\Python\Python39";
                string pythonDll = Path.Combine(pythonHome, "python39.dll");

                if (!File.Exists(pythonDll))
                    throw new FileNotFoundException($"Python DLL not found at {pythonDll}");

                Console.WriteLine($"Python Home: {pythonHome}");
                Console.WriteLine($"Python DLL: {pythonDll}");

                // Required environment variables BEFORE PythonEngine.Initialize()
                Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", pythonDll);
                Environment.SetEnvironmentVariable("PYTHONHOME", pythonHome);

                string pythonPath = string.Join(";",
                    Path.Combine(pythonHome, "Lib"),
                    Path.Combine(pythonHome, "Lib", "site-packages")
                );

                Environment.SetEnvironmentVariable("PYTHONPATH", pythonPath);

                // Initialize PythonEngine on UI thread (required for WPF)
                PythonEngine.Initialize();
                _isInitialized = true;

                Console.WriteLine("Python runtime initialized successfully.");

                // Validate Python with a simple import
                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    Console.WriteLine($"Python version: {sys.version}");
                    Console.WriteLine("Python sys.path:");
                    foreach (var p in sys.path)
                        Console.WriteLine($"  {p}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error initializing Python engine: " + ex);
                throw;
            }
        }

        public void Dispose()
        {
            if (_isInitialized)
            {
                PythonEngine.Shutdown();
                _isInitialized = false;
            }
        }
    }
}
