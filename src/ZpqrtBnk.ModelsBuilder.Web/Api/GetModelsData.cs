using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Our.ModelsBuilder.Web.Api
{
    [DataContract]
    public class GetModelsData : ValidateClientVersionData
    {
        [DataMember]
        public string Namespace { get; set; }

        [DataMember]
        public IDictionary<string, string> Files { get; set; }

        public override bool IsValid => base.IsValid && Files != null;
    }
}