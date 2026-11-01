using System;
using System.Collections.Generic;
using MedSimXR.Core;

namespace MedSimXR.Equipment
{
    [Serializable]
    public class EquipmentEntry
    {
        public string id;
        public string displayName;
        public string category;
        public string description;
        public string trainingUse;
        public string labelText;
        public string source;          // "simready" or "placeholder"
        public string simreadyId;
        public string sourceNote;
        public string clinicalReview;  // "pending" until a qualified reviewer signs off
        public bool canGrab;
        public bool canInspect;
        public bool isFixed;

        public bool IsPlaceholder { get { return source == "placeholder"; } }
    }

    [Serializable]
    public class EquipmentDatabaseFile
    {
        public int schemaVersion;
        public string contentStatus;
        public EquipmentEntry[] items;
    }

    public class EquipmentRegistry
    {
        readonly Dictionary<string, EquipmentEntry> _byId = new Dictionary<string, EquipmentEntry>();
        public string ContentStatus { get; private set; }
        public IEnumerable<EquipmentEntry> All { get { return _byId.Values; } }

        public static EquipmentRegistry LoadDefault()
        {
            return From(DataLoader.Load<EquipmentDatabaseFile>("equipment"));
        }

        public static EquipmentRegistry From(EquipmentDatabaseFile file)
        {
            var r = new EquipmentRegistry { ContentStatus = file.contentStatus };
            foreach (var e in file.items)
            {
                if (r._byId.ContainsKey(e.id)) throw new InvalidOperationException("Duplicate equipment id: " + e.id);
                r._byId[e.id] = e;
            }
            return r;
        }

        public bool TryGet(string id, out EquipmentEntry entry) { return _byId.TryGetValue(id ?? "", out entry); }

        public string NameOf(string id)
        {
            EquipmentEntry e;
            return TryGet(id, out e) ? e.displayName : id;
        }
    }
}
