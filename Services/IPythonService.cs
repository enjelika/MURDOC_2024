using System.Threading.Tasks;

namespace MURDOC_2024.Services
{
    public interface IPythonService
    {
        /// <summary>Writes sensitivity (d') and bias (β) parameters to the MICA config file read by Python.</summary>
        void SetDetectionParameters(double sensitivity, double bias);

        /// <summary>Runs IAI_Decision_Hierarchy.py without MICA parameters (baseline bypass mode).</summary>
        Task<string> RunIAIModelsBypassAsync(string imagePath);

        /// <summary>Runs IAI_Decision_Hierarchy.py with current MICA sensitivity and bias arguments.</summary>
        Task<string> RunIAIModelsWithMICAAsync(string imagePath);
    }
}
