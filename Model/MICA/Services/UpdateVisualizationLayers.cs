using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.MICA.Services
{
    public class UpdateVisualizationLayers
    {
        private readonly IRankNetService _rankNetService;

        public UpdateVisualizationLayers(IRankNetService rankNetService)
        {
            _rankNetService = rankNetService;
        }

        public async Task UpdateLayers(UserModification userModification)
        {
            // Apply user modifications to the RankNet model
            var updatedRankNetOutput = await _rankNetService.ApplyUserModification(userModification);

            // Update the RankNet visualization layer based on the modified output
            UpdateRankNetVisualizationLayer(updatedRankNetOutput);
        }

        private void UpdateRankNetVisualizationLayer(RankNetOutput output)
        {
            // Update the RankNet visualization layer based on the modified output
            // This may involve updating the segmentation map, heatmap, or other visual elements
            // You can use the VisualizationService or other relevant classes to update the UI
        }
    }
}
