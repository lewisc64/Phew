using MongoDB.Bson;
using System;
using System.Net.Http;

namespace Phew
{
    public class Light
    {
        private string _name;

        private string _effect;

        private bool _on;

        private int _brightness;

        private int _saturation;

        private int _hue;

        public bool AutoUpdateState { get; set; } = true;

        public int Number { get; private set; }

        public Bridge Bridge { get; private set; }

        public string Name
        {
            get
            {
                return _name;
            }

            set
            {
                _name = value;
                Update(new BsonDocument
                {
                    { "name", _name },
                });
            }
        }

        public string Effect
        {
            get
            {
                return _effect;
            }

            set
            {
                _effect = value;
                if (AutoUpdateState)
                {
                    UpdateState(new BsonDocument
                    {
                        { "effect", _effect },
                    });
                }
            }
        }

        public bool On
        {
            get
            {
                return _on;
            }
            set
            {
                _on = value;
                if (AutoUpdateState)
                {
                    UpdateState(new BsonDocument
                    {
                        { "on", _on },
                    });
                }
            }
        }

        public double Brightness // %
        {
            get
            {
                return Math.Min(_brightness * 100 / 254D, 100);
            }
            set
            {
                _brightness = (int)(254 * value / 100);
                if (AutoUpdateState)
                {
                    UpdateState(new BsonDocument
                    {
                        { "bri", _brightness },
                    });
                }
            }
        }

        public double Saturation // %
        {
            get
            {
                return Math.Min(_saturation * 100 / 254D, 100);
            }
            set
            {
                _saturation = (int)(254 * value / 100);
                if (AutoUpdateState)
                {
                    UpdateState(new BsonDocument
                    {
                        { "sat", _saturation },
                    });
                }
            }
        }

        public double Hue // degrees
        {
            get
            {
                return _hue / 65535D * 360;
            }
            set
            {
                _hue = (int)(65535 * ((value % 360) / 360));
                if (AutoUpdateState)
                {
                    UpdateState(new BsonDocument
                    {
                        { "hue", _hue },
                    });
                }
            }
        }

        public int? TransitionTime { get; set; } = null;

        public Light(Bridge bridge, int number)
        {
            Bridge = bridge;
            Number = number;
        }

        public void SetFromDocument(BsonDocument document)
        {
            _on = document["state"]["on"].AsBoolean;
            _brightness = document["state"]["bri"].AsInt32;
            _hue = document["state"]["bri"].AsInt32;
            _saturation = document["state"]["sat"].AsInt32;

            _name = document["name"].AsString;
        }

        public void UpdateState()
        {
            if (AutoUpdateState)
            {
                throw new InvalidOperationException("State is auto-updating.");
            }
            UpdateState(new BsonDocument
            {
                { "on", _on },
                { "effect", _effect },
                { "hue", _hue },
                { "sat", _saturation },
                { "bri", _brightness },
            });
        }

        private void UpdateState(BsonDocument data)
        {
            if (TransitionTime != null)
            {
                data["transitiontime"] = TransitionTime.Value;
            }
            Bridge.SendApiRequest(HttpMethod.Put, $"api/{Bridge.Username}/lights/{Number}/state", data);
        }

        private void Update(BsonDocument data)
        {
            Bridge.SendApiRequest(HttpMethod.Put, $"api/{Bridge.Username}/lights/{Number}", data);
        }
    }
}
