using System.Threading.Tasks;

namespace MURDOC_2024.Services
{
    public interface IPythonService
    {
        void SetDetectionParameters(double sensitivity, double bias);

        Task<string> RunIAIModelsBypassAsync(string imagePath);
        Task<string> RunIAIModelsWithMICAAsync(string imagePath);
    }
}
