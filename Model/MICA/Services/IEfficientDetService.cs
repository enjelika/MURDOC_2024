using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.MICA.Services
{
    public interface IEfficientDetService
    {
        Task<EfficientDetOutput> ApplyUserModification(UserModification userModification);
        Task<EfficientDetOutput> RunEfficientDetModel(EfficientDetInput input);
    }
}
