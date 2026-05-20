using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MozaDevicesPlugin.Models
{
    internal sealed class SimHubControlMapperSettingsStatus
    {
        public bool Available { get; private set; }

        public string Path { get; private set; } = "";

        public string Error { get; private set; } = "";

        public bool AllowVJoyAsSource { get; private set; }

        public bool RecognizeIndividualWheels { get; private set; }

        public int OutputMode { get; private set; }

        public int OutputVJoyDeviceId { get; private set; }

        public bool IsVJoyOutputMode => OutputMode == 1;

        public static SimHubControlMapperSettingsStatus ReadFromSimHub()
        {
            var status = new SimHubControlMapperSettingsStatus
            {
                Path = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "PluginsData",
                    "Common",
                    "ControlMapperPlugin.GeneralSettingsV2.json")
            };

            try
            {
                if (!File.Exists(status.Path))
                {
                    status.Error = "Control Mapper settings file was not found.";
                    return status;
                }

                string json = File.ReadAllText(status.Path);
                var serializer = new DataContractJsonSerializer(typeof(ControlMapperSettingsDto));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var dto = (ControlMapperSettingsDto?)serializer.ReadObject(stream);
                    if (dto != null)
                    {
                        status.AllowVJoyAsSource = dto.AllowVJoyAsSource;
                        status.RecognizeIndividualWheels = dto.RecognizeIndividualWheels;
                        status.OutputMode = dto.OutputMode;
                        status.OutputVJoyDeviceId = dto.VJoyMapping?.TargetVJoyId ?? 0;
                    }
                }

                status.Available = true;
            }
            catch (Exception ex)
            {
                status.Error = ex.GetType().Name + ": " + ex.Message;
            }

            return status;
        }

        [DataContract]
        private sealed class ControlMapperSettingsDto
        {
            [DataMember(Name = "AllowVJOYAsAsource")]
            public bool AllowVJoyAsSource { get; set; }

            [DataMember(Name = "RecognizeIndiviualWheels")]
            public bool RecognizeIndividualWheels { get; set; }

            [DataMember(Name = "OutputMode")]
            public int OutputMode { get; set; }

            [DataMember(Name = "VJoyMapping")]
            public VJoyMappingDto? VJoyMapping { get; set; }
        }

        [DataContract]
        private sealed class VJoyMappingDto
        {
            [DataMember(Name = "TargetVJoyId")]
            public int TargetVJoyId { get; set; }
        }
    }
}
