using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NAADF.World.Model
{

    public class ModelHandler
    {
        public List<ModelData> models;

        public ModelHandler()
        {
            models = new();
        }

        public void AddModel(ModelData model)
        {
            models.Add(model);
        }
    }

}
