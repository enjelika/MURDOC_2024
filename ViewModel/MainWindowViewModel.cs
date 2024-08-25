using ImageProcessor;
using Microsoft.Win32;
using Python.Runtime; // Ensure this namespace is recognized without errors
using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.ViewModel
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        #region Private Variables

        private string _selectedImageFileName;  // filename with file type

        private string _selectedImageName;      // filename without file type

        private BitmapImage _selectedImage;

        private BitmapImage _previewImage;

        private readonly ICommand _exitCommand;

        private readonly ICommand _newCommand;

        private readonly ICommand _saveCommand;

        private readonly ICommand _openCommand;

        private readonly ICommand _browseCommand;

        private readonly ICommand _selectedImageCommand;

        private readonly ICommand _runCommand;

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;

        #region ICommands

        public ICommand ExitCommand => _exitCommand;

        public ICommand NewCommand => _newCommand;

        public ICommand SaveCommand => _saveCommand;

        public ICommand OpenCommand => _openCommand;

        public ICommand BrowseCommand => _browseCommand;

        public ICommand SelectedImageCommand => _selectedImageCommand;

        public ICommand RunCommand => _runCommand;

        #endregion

        private string _iaiOutputMessage;
        private string _selectedImagePath;
        private int _sliderBrightness;
        private int _sliderContrast;
        private int _sliderSaturation;
        private MemoryStream _modifiedImageStream;
        private bool hasUserModifiedImage;

        /// <summary>
        /// Getter/Setter for the IAI Output Message
        /// </summary>
        public string IAIOutputMessage
        {
            get { return _iaiOutputMessage; }
            set
            {
                _iaiOutputMessage = value;
                OnPropertyChanged(nameof(IAIOutputMessage));
            }
        }

        /// <summary>
        /// Getter/Setter for the user selected image path.
        /// </summary>
        public string SelectedImagePath
        {
            get { return _selectedImagePath; }
            set
            {
                _selectedImagePath = value;
                OnPropertyChanged(nameof(SelectedImagePath));
                UpdateSelectedImageFileName(); // Update SelectedImageFileName when SelectedImagePath changes

                // Enable or disable the Run button based on whether an image is selected
                IsRunButtonEnabled = !string.IsNullOrEmpty(value);
            }
        }

        /// <summary>
        /// Getter/Setter for the user selected image file name.
        /// </summary>
        public string SelectedImageFileName
        {
            get => _selectedImageFileName;
            private set
            {
                _selectedImageFileName = value;
                OnPropertyChanged(nameof(SelectedImageFileName));
            }
        }

        /// <summary>
        /// Getter/Setter for the user selected image file name without the file extension.
        /// </summary>
        public string SelectedImageName
        {
            get => _selectedImageName;
            private set
            {
                _selectedImageName = value;
                OnPropertyChanged(nameof(SelectedImageName));
            }
        }

        /// <summary>
        /// Returns the user selected image to be displayed on the GUI.
        /// </summary>
        public BitmapImage SelectedImage
        {
            get { return _selectedImage; }
            set
            {
                _selectedImage = value;
                OnPropertyChanged(nameof(SelectedImage));

                // TODO: Reset all Model Traversal Progress circles to empty

                // TODO: Clear all of the Model Traversal Results - except for Input Image
            }
        }

        /// <summary>
        /// Returns the preview/mouse-over image to be displayed on the GUI.
        /// </summary>
        public BitmapImage PreviewImage
        {
            get { return _previewImage; }
            set
            {
                _previewImage = value;
                OnPropertyChanged(nameof(PreviewImage));
            }
        }

        public BitmapImage NoSelectedImage
        {
            get { return new BitmapImage(new Uri("pack://application:,,,/MURDOC;component/Assets/image_placeholder.png")); ; }
            set { }
        }

        public int SliderBrightness
        {
            get
            {
                return _sliderBrightness;
            }
            set
            {
                _sliderBrightness = value;
                UpdateSelectedImage(_sliderBrightness, _sliderContrast, _sliderSaturation);
            }
        }

        public int SliderContrast
        {
            get
            {
                return _sliderContrast;
            }
            set
            {
                _sliderContrast = value;
                UpdateSelectedImage(_sliderBrightness, _sliderContrast, _sliderSaturation);
            }
        }

        public int SliderSaturation
        {
            get
            {
                return _sliderSaturation;
            }
            set
            {
                _sliderSaturation = value;
                UpdateSelectedImage(_sliderBrightness, _sliderContrast, _sliderSaturation);
            }
        }

        #region ResNet50 Off-Ramp Images
        /// <summary>
        /// Setter will set the appropriate off-ramp images dependant on:
        ///         (1) if the model has run
        ///         (2) if the off-ramp image exists in the appropriate folder
        /// </summary>
        private string _resNet50ConvImagePath;
        public string ResNet50ConvImagePath
        {
            get { return _resNet50ConvImagePath; }
            set
            {
                if (_resNet50ConvImagePath != value)
                {
                    _resNet50ConvImagePath = value;
                    OnPropertyChanged(nameof(ResNet50ConvImagePath));

                    // Load and set the image directly to ResNet50Conv
                    LoadResNet50ConvImage();
                }
            }
        }

        private BitmapImage _resNet50ConvImage;
        public BitmapImage ResNet50Conv
        {
            get { return _resNet50ConvImage; }
            set
            {
                _resNet50ConvImage = value;
                OnPropertyChanged(nameof(ResNet50Conv));
            }
        }

        private BitmapImage _resNet50Layer1;
        public BitmapImage ResNet50Layer1
        {
            get => _resNet50Layer1;
            set
            {
                if (_resNet50Layer1 != value)
                {
                    _resNet50Layer1 = value;
                    OnPropertyChanged(nameof(ResNet50Layer1));
                }
            }
        }

        private string _resNet50Layer1ImagePath;
        public string ResNet50Layer1ImagePath
        {
            get => _resNet50Layer1ImagePath;
            set
            {
                if (_resNet50Layer1ImagePath != value)
                {
                    _resNet50Layer1ImagePath = value;
                    OnPropertyChanged(nameof(ResNet50Layer1ImagePath));

                    // Load and set the image directly to ResNet50Layer1
                    LoadResNet50Layer1Image();
                }
            }
        }

        private BitmapImage _resNet50Layer2;
        public BitmapImage ResNet50Layer2
        {
            get => _resNet50Layer2;
            set
            {
                if (_resNet50Layer2 != value)
                {
                    _resNet50Layer2 = value;
                    OnPropertyChanged(nameof(ResNet50Layer2));
                }
            }
        }

        private string _resNet50Layer2ImagePath;
        public string ResNet50Layer2ImagePath
        {
            get => _resNet50Layer2ImagePath;
            set
            {
                if (_resNet50Layer2ImagePath != value)
                {
                    _resNet50Layer2ImagePath = value;
                    OnPropertyChanged(nameof(ResNet50Layer2ImagePath));

                    // Load and set the image directly to ResNet50Layer2
                    LoadResNet50Layer2Image();
                }
            }
        }

        private BitmapImage _resNet50Layer3;
        public BitmapImage ResNet50Layer3
        {
            get => _resNet50Layer3;
            set
            {
                if (_resNet50Layer3 != value)
                {
                    _resNet50Layer3 = value;
                    OnPropertyChanged(nameof(ResNet50Layer3));
                }
            }
        }

        private string _resNet50Layer3ImagePath;
        public string ResNet50Layer3ImagePath
        {
            get => _resNet50Layer3ImagePath;
            set
            {
                if (_resNet50Layer3ImagePath != value)
                {
                    _resNet50Layer3ImagePath = value;
                    OnPropertyChanged(nameof(ResNet50Layer3ImagePath));

                    // Load and set the image directly to ResNet50Layer3
                    LoadResNet50Layer3Image();
                }
            }
        }

        private BitmapImage _resNet50Layer4;
        public BitmapImage ResNet50Layer4
        {
            get => _resNet50Layer4;
            set
            {
                if (_resNet50Layer4 != value)
                {
                    _resNet50Layer4 = value;
                    OnPropertyChanged(nameof(ResNet50Layer4));
                }
            }
        }

        private string _resNet50Layer4ImagePath;
        public string ResNet50Layer4ImagePath
        {
            get => _resNet50Layer4ImagePath;
            set
            {
                if (_resNet50Layer4ImagePath != value)
                {
                    _resNet50Layer4ImagePath = value;
                    OnPropertyChanged(nameof(ResNet50Layer4ImagePath));

                    // Load and set the image directly to ResNet50Layer4
                    LoadResNet50Layer4Image();
                }
            }
        }

        private BitmapImage _resNet50Output;
        public BitmapImage ResNet50Output
        {
            get => _resNet50Output;
            set
            {
                if (_resNet50Output != value)
                {
                    _resNet50Output = value;
                    OnPropertyChanged(nameof(ResNet50Output));
                }
            }
        }

        private string _resNet50OutputImagePath;
        public string ResNet50OutputImagePath
        {
            get => _resNet50OutputImagePath;
            set
            {
                if (_resNet50OutputImagePath != value)
                {
                    _resNet50OutputImagePath = value;
                    OnPropertyChanged(nameof(ResNet50OutputImagePath));

                    // Load and set the image directly to ResNet50Layer4
                    LoadResNet50OutputImage();
                }
            }
        }

        private string _rankNetFixationDecoderImagePath;
        public string RankNetFixationDecoderImagePath
        {
            get => _rankNetFixationDecoderImagePath;
            set
            {
                if (_rankNetFixationDecoderImagePath != value)
                {
                    _rankNetFixationDecoderImagePath = value;
                    OnPropertyChanged(nameof(RankNetFixationDecoderImagePath));
                }
            }
        }

        private string _rankNetCamouflageDecoderImagePath;
        public string RankNetCamouflageDecoderImagePath
        {
            get => _rankNetCamouflageDecoderImagePath;
            set
            {
                if (_rankNetCamouflageDecoderImagePath != value)
                {
                    _rankNetCamouflageDecoderImagePath = value;
                    OnPropertyChanged(nameof(RankNetCamouflageDecoderImagePath));
                }
            }
        }

        private string _facePredictionImagePath;
        public string FACEPredictionImagePath
        {
            get => _facePredictionImagePath;
            set
            {
                if (_facePredictionImagePath != value)
                {
                    _facePredictionImagePath = value;
                    OnPropertyChanged(nameof(FACEPredictionImagePath));
                }
            }
        }

        private string _weakAreaCamoImagePath;
        public string WeakAreaCamoImagePath
        {
            get => _weakAreaCamoImagePath;
            set
            {
                if (_weakAreaCamoImagePath != value)
                {
                    _weakAreaCamoImagePath = value;
                    OnPropertyChanged(nameof(WeakAreaCamoImagePath));
                }
            }
        }
        #endregion

        /// <summary>
        /// Disables/Enables the Run button 
        /// Relies upon if a user selects an image or not
        /// </summary>
        private bool _isRunButtonEnabled;
        public bool IsRunButtonEnabled
        {
            get { return _isRunButtonEnabled; }
            set
            {
                _isRunButtonEnabled = value;
                OnPropertyChanged(nameof(IsRunButtonEnabled));
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public MainWindowViewModel()
        {
            IsRunButtonEnabled = false; // Disable Run button initially

            _modifiedImageStream = new MemoryStream();
            // Create Temporary Folder Location
            Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp"));

            LoadImage();

            LoadResNet50ConvImage();
            LoadResNet50Layer1Image();
            LoadResNet50Layer2Image();
            LoadResNet50Layer3Image();
            LoadResNet50Layer4Image();
            LoadResNet50OutputImage();

            _exitCommand = new RelayCommand(ExecuteExitCommand);

            _browseCommand = new RelayCommand(ExecuteBrowseCommand);
            _selectedImageCommand = new RelayCommand(LoadImage);

            _runCommand = new RelayCommand(ExecuteRunCommand);
        }

        /// <summary>
        /// Closes the application.
        /// </summary>
        private void ExecuteExitCommand()
        {
            Console.WriteLine("In ExecuteExitCommand()");

            // Add logic to exit the application
            Environment.Exit(0);
        }

        /// <summary>
        /// 
        /// </summary>
        private void ExecuteNewCommand()
        {
            // TODO: Add logic for new command - reset everything on the screen
        }

        /// <summary>
        /// 
        /// </summary>
        private void ExecuteOpenCommand()
        {
            // TODO: Add logic for open command
        }

        /// <summary>
        /// 
        /// </summary>
        private void ExecuteSaveCommand()
        {
            // TODO: Add logic for save command - Save the models 'visualization' as a PDF
        }

        /// <summary>
        /// Executes the BrowseCommand to open a file dialog for selecting an image file.
        /// </summary>
        private void ExecuteBrowseCommand()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp";
            if (openFileDialog.ShowDialog() == true)
            {
                hasUserModifiedImage = false;
                SelectedImagePath = openFileDialog.FileName;
                LoadImage();
            }
        }

        /// <summary>
        /// Run the FACE model (submodels include ResNet50, RankNet, and EfficientDet-D7)
        /// </summary>
        private void ExecuteRunCommand()
        {
            // Need to handle the scenario by preventing the run when SelectedImagePath has no image selected - done: disabled the run models button if no image is selected

            // Initialize Python engine
            if (!PythonEngine.IsInitialized)
            {
                InitializePythonEngine();
            }

            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                dynamic os = Py.Import("os");

                // Add the directory containing your Python script to Python's sys.path
                string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\", "Model", "IAI_Decision_Hierarchy.py");
                sys.path.append(os.path.dirname(scriptPath));

                // ResNet50
                try
                {
                    // Import your Python script module
                    dynamic script = Py.Import("IAI_Decision_Hierarchy");

                    // Save Temporary Image if user modified
                    string tempImageLocation = SaveTemporaryImage();
                    string imageLocation = string.IsNullOrEmpty(tempImageLocation) ? SelectedImagePath : tempImageLocation;

                    // Call the iaiDecision_test function from your Python script
                    string message = script.iaiDecision_test(imageLocation); //IAIOutputMessage
                    IAIOutputMessage = message;
                    OnPropertyChanged(nameof(IAIOutputMessage));

                    string executableDir = AppDomain.CurrentDomain.BaseDirectory;
                    string folderPath = Path.Combine(executableDir, "resnet50_output", _selectedImageName);

                    // Update the ResNet50ConvImagePath to trigger UI update
                    string initConvImagePath = Path.Combine(folderPath, _selectedImageName + "_initial_conv_feature_map.png");
                    ResNet50ConvImagePath = initConvImagePath;
                    OnPropertyChanged(nameof(ResNet50Conv)); // Trigger UI update for ResNet50Conv

                    string layer1ImagePath = Path.Combine(folderPath, _selectedImageName + "_stage1_feature_map.png");
                    Console.WriteLine(layer1ImagePath);
                    ResNet50Layer1ImagePath = layer1ImagePath;
                    OnPropertyChanged(nameof(ResNet50Layer1));

                    string layer2ImagePath = Path.Combine(folderPath, _selectedImageName + "_stage2_feature_map.png");
                    ResNet50Layer2ImagePath = layer2ImagePath;
                    OnPropertyChanged(nameof(ResNet50Layer2));

                    string layer3ImagePath = Path.Combine(folderPath, _selectedImageName + "_stage3_feature_map.png");
                    ResNet50Layer3ImagePath = layer3ImagePath;
                    OnPropertyChanged(nameof(ResNet50Layer3));

                    string layer4ImagePath = Path.Combine(folderPath, _selectedImageName + "_stage4_feature_map.png");
                    ResNet50Layer4ImagePath = layer4ImagePath;
                    OnPropertyChanged(nameof(ResNet50Layer4));

                    string fixationDecoderImagePath = Path.Combine(folderPath, _selectedImageName + "_fixation_decoder.png");
                    RankNetFixationDecoderImagePath = fixationDecoderImagePath;
                    OnPropertyChanged(nameof(RankNetFixationDecoderImagePath));

                    string camouflageDecoderImagePath = Path.Combine(folderPath, _selectedImageName + "_camouflage_decoder.png");
                    RankNetCamouflageDecoderImagePath = camouflageDecoderImagePath;
                    OnPropertyChanged(nameof(RankNetCamouflageDecoderImagePath));

                    string weakAreaCamoImagePath = Path.Combine(folderPath, _selectedImageName + "_weak_area_camo.png");
                    WeakAreaCamoImagePath = weakAreaCamoImagePath;
                    OnPropertyChanged(nameof(WeakAreaCamoImagePath));

                    string facePredictionImagePath = Path.Combine(folderPath, "segmented_" + _selectedImageName + ".jpg");
                    FACEPredictionImagePath = facePredictionImagePath;
                    OnPropertyChanged(nameof(FACEPredictionImagePath));

                    string outputImagePath = Path.Combine(folderPath, _selectedImageName + "_prediction.png");
                    ResNet50OutputImagePath = outputImagePath;
                    OnPropertyChanged(nameof(ResNet50Output));



                }
                catch (PythonException exception)
                {
                    // Probably should indicate somewhere on the GUI that something went wrong
                    Console.WriteLine("Exception occured: " + exception);
                }

            }
        }

        /// <summary>
        /// Initializes Python Engine with the default Visual Studio Python DLL location
        /// </summary>
        private void InitializePythonEngine()
        {
            try
            {
                string pathToVirtualEnv = @"C:\Users\pharm\AppData\Local\Programs\Python\Python39"; //@"C:\Users\pharm\anaconda3\envs\murdoc\";

                string pythonDll = Environment.GetEnvironmentVariable("PythonDLL", EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", pythonDll);
                Environment.SetEnvironmentVariable("PYTHONHOME", pathToVirtualEnv, EnvironmentVariableTarget.Process);
                Environment.SetEnvironmentVariable("PYTHONPATH", $"{pathToVirtualEnv}\\Lib\\site-packages;{pathToVirtualEnv}\\Lib", EnvironmentVariableTarget.Process);

                // Initialize will fail if configuration manager is not set up or ran with x64 since the above python Dll is 64 bit
                PythonEngine.Initialize();
            }
            catch (TypeInitializationException tiex)
            {
                // Log or display the type initialization exception message and inner exception
                Console.WriteLine("Type Initialization Exception: " + tiex.Message);
                Console.WriteLine("Inner Exception: " + tiex.InnerException?.Message);
            }
            catch (Exception ex)
            {
                // Log or display any other exceptions during initialization
                Console.WriteLine("Error initializing Python engine: " + ex.Message);
            }
        }

        /// <summary>
        /// Updates the selected image with user defined brightnesss, contrast, and saturation values
        /// </summary>
        /// <param name="brightness">User Defined Brightness</param>
        /// <param name="contrast">User Defined Contrast</param>
        /// <param name="saturation">User Defined Saturation</param>
        private void UpdateSelectedImage(int brightness, int contrast, int saturation)
        {
            if (!string.IsNullOrEmpty(SelectedImagePath))
            {
                byte[] imageBytes = File.ReadAllBytes(SelectedImagePath);
                using (MemoryStream inStream = new MemoryStream(imageBytes))
                {
                    using (ImageFactory imageFactory = new ImageFactory(preserveExifData: true))
                    {
                        hasUserModifiedImage = true;
                        imageFactory.Load(inStream);
                        imageFactory.Brightness(brightness);
                        imageFactory.Contrast(contrast);
                        imageFactory.Saturation(saturation);
                        imageFactory.Save(_modifiedImageStream);
                        LoadImage();
                    }
                }
            }
        }

        /// <summary>
        /// Saves the temporary image to a temp folder in JPG format
        /// </summary>
        /// <returns>String of location saved</returns>
        private string SaveTemporaryImage()
        {
            string tempPath = string.Empty;
            if (hasUserModifiedImage)
            {
                byte[] imageBytes = File.ReadAllBytes(SelectedImagePath);
                using (MemoryStream inStream = new MemoryStream(imageBytes))
                {
                    using (ImageFactory imageFactory = new ImageFactory(preserveExifData: true))
                    {
                        tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp", SelectedImageName + ".jpg");
                        imageFactory.Load(inStream);
                        imageFactory.Brightness(_sliderBrightness);
                        imageFactory.Contrast(_sliderContrast);
                        imageFactory.Saturation(_sliderSaturation);
                        // Delete old temp file if exists since it won't overwrite
                        File.Delete(tempPath);
                        imageFactory.Save(tempPath);
                    }
                }
            }
            return tempPath;
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadImage()
        {
            if (hasUserModifiedImage)
            {
                BitmapImage tempModifiedImage = new BitmapImage();
                tempModifiedImage.BeginInit();
                tempModifiedImage.CacheOption = BitmapCacheOption.OnLoad;
                tempModifiedImage.StreamSource = _modifiedImageStream;
                tempModifiedImage.EndInit();
                SelectedImage = tempModifiedImage;
            }
            else if (!string.IsNullOrEmpty(SelectedImagePath))
            {
                SelectedImage = new BitmapImage(new Uri(SelectedImagePath));
            }
            else
            {
                // Set the default placeholder image
                SelectedImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadResNet50ConvImage()
        {
            if (!string.IsNullOrEmpty(ResNet50ConvImagePath))
            {
                ResNet50Conv = new BitmapImage(new Uri(ResNet50ConvImagePath));
            }
            else
            {
                // Set the default placeholder image
                ResNet50Conv = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadResNet50Layer1Image()
        {
            if (!string.IsNullOrEmpty(ResNet50Layer1ImagePath))
            {
                Console.WriteLine("Image exists.");
                ResNet50Layer1 = new BitmapImage(new Uri(ResNet50Layer1ImagePath));
            }
            else
            {
                // Set the default placeholder image
                ResNet50Layer1 = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadResNet50Layer2Image()
        {
            if (!string.IsNullOrEmpty(ResNet50Layer2ImagePath))
            {
                ResNet50Layer2 = new BitmapImage(new Uri(ResNet50Layer2ImagePath));
            }
            else
            {
                // Set the default placeholder image
                ResNet50Layer2 = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadResNet50Layer3Image()
        {
            if (!string.IsNullOrEmpty(ResNet50Layer3ImagePath))
            {
                ResNet50Layer3 = new BitmapImage(new Uri(ResNet50Layer3ImagePath));
            }
            else
            {
                // Set the default placeholder image
                ResNet50Layer3 = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadResNet50Layer4Image()
        {
            if (!string.IsNullOrEmpty(ResNet50Layer4ImagePath))
            {
                ResNet50Layer4 = new BitmapImage(new Uri(ResNet50Layer4ImagePath));
            }
            else
            {
                // Set the default placeholder image
                ResNet50Layer4 = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        private void LoadResNet50OutputImage()
        {
            if (!string.IsNullOrEmpty(ResNet50OutputImagePath))
            {
                ResNet50Output = new BitmapImage(new Uri(ResNet50OutputImagePath));
            }
            else
            {
                // Set the default placeholder image
                ResNet50Output = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
            }
        }

        /// <summary>
        /// Updates the GUI to display the user selected image file name
        /// </summary>
        private void UpdateSelectedImageFileName()
        {
            if (SelectedImagePath != "Assets/image_placeholder.png")
            {
                SelectedImageFileName = Path.GetFileName(SelectedImagePath);
                SelectedImageName = Path.GetFileNameWithoutExtension(SelectedImagePath);
            }
        }

        /// <summary>
        /// Handles the changing of the preview image upon mouse over of a process image.
        /// </summary>
        /// <param name="imagePath">The image that is moused over.</param>
        /// <remarks>
        /// </remarks>
        public void HandlePreviewImageChanged(string imagePath)
        {
            PreviewImage = new BitmapImage(new Uri(imagePath));
        }

        /// <summary>
        /// Invokes the PropertyChanged event to notify subscribers of a property change.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}