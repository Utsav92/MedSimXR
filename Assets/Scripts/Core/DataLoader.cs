using System;
using UnityEngine;

namespace MedSimXR.Core
{
    /// <summary>
    /// Loads JSON content from Resources/Data. The source of truth is the top-level Data/ folder;
    /// the editor build step copies it into Assets/Resources/Data so it also works on Android.
    /// </summary>
    public static class DataLoader
    {
        public static T Load<T>(string resourceName)
        {
            var asset = Resources.Load<TextAsset>("Data/" + resourceName);
            if (asset == null)
                throw new InvalidOperationException("Data file not found in Resources/Data: " + resourceName);
            return JsonUtility.FromJson<T>(asset.text);
        }
    }
}
