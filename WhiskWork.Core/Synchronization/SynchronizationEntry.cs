using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WhiskWork.Core.Synchronization
{
    public class SynchronizationEntry
    {
        private readonly string _id;
        private readonly string _status;
        private readonly Dictionary<string, string> _properties;


        public SynchronizationEntry(string id, string status, Dictionary<string,string> properties)
        {
            _id = id;
            _status = status;
            _properties = properties;
        }

        public string Id
        {
            get { return _id; }
        }

        public string Status
        {
            get { return _status; }
        }

        public Dictionary<string, string> Properties
        {
            get { return new Dictionary<string, string>(_properties); }
        }

        public override bool Equals(object obj)
        {
            if (!(obj is SynchronizationEntry))
            {
                return false;
            }

            var entry = (SynchronizationEntry)obj;

            var result = true;

            result &= _id == entry._id;
            result &= _status == entry._status;
            result &= _properties.SequenceEqual(entry._properties);

            return result;
        }

        public override int GetHashCode()
        {
            var hc = _id != null ? _id.GetHashCode() : 1;
            hc ^= _status != null ? _status.GetHashCode() : 2;
            hc ^= _properties.Count > 0 ? _properties.Select(kv => kv.Key.GetHashCode() ^ kv.Value.GetHashCode()).Aggregate((hash, next) => hash ^ next) : 4;

            return hc;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("Id={0},", _id);
            sb.AppendFormat("Status={0},", _status);
            sb.AppendFormat("Properties={0}", _properties.Count() > 0 ? _properties.Select(kv => kv.Key + ":" + kv.Value).Aggregate((current, next) => current + "&" + next) : string.Empty);

            return sb.ToString();
        }
    }
}