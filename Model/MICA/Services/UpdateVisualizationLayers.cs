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
        private readonly IEfficientDetService _efficientDetService;

        public UpdateVisualizationLayers(IRankNetService rankNetService, IEfficientDetService efficientDetService)
        {
            _rankNetService = rankNetService;
            _efficientDetService = efficientDetService;
        }

        public async Task UpdateLayers(UserModification userModification)
        {
            // Apply user modifications to the RankNet model
            var updatedRankNetOutput = await _rankNetService.ApplyUserModification(userModification);

            // Apply user modifications to the EfficientDetD7 model
            var updatedEfficientDetOutput = await _efficientDetService.ApplyUserModification(userModification);

            // Update the visualization layers based on the modified outputs
            UpdateRankNetVisualizationLayer(updatedRankNetOutput);
            UpdateEfficientDetVisualizationLayer(updatedEfficientDetOutput);
        }

        private void UpdateRankNetVisualizationLayer(RankNetOutput output)
        {
            // Update the RankNet visualization layer based on the modified output
            // This may involve updating the segmentation map, heatmap, or other visual elements
            // You can use the VisualizationService or other relevant classes to update the UI
        }

        private void UpdateEfficientDetVisualizationLayer(EfficientDetOutput output)
        {
            // Update the EfficientDetD7 visualization layer based on the modified output
            // This may involve updating bounding boxes, labels, or other visual elements
            // You can use the VisualizationService or other relevant classes to update the UI
        }
    }
}
