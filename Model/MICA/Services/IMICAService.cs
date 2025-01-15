using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MURDOC_2024.Model.MICA.Services
{
    public interface IMICAService
    {
        Task<MICAOutput> ProcessUserModification(UserModification userModification);
        Task<MICAOutput> RunMICAModel(MICAInput input);
    }
}
