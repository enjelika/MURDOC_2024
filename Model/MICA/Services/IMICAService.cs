using System.Threading.Tasks;

namespace MURDOC_2024.Model.MICA.Services
{
    /// <summary>
    /// Interface defining the contract for Mixed-Initiative Camouflage Analysis (MICA) services
    /// </summary>
    public interface IMICAService
    {
        /// <summary>
        /// Processes the results from the IAI decision hierarchy analysis
        /// </summary>
        /// <param name="iaiResults">The analysis results from the IAI decision hierarchy process</param>
        /// <returns>A Task containing the processed DetectionResult with combined analysis data</returns>
        Task<DetectionResult> ProcessResults(IAIResults iaiResults);
    }
}