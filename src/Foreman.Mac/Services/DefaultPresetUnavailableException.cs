using System;

namespace Foreman.Mac.Services {
    public sealed class DefaultPresetUnavailableException : Exception {
        public DefaultPresetUnavailableException()
            : base($"The default preset ({PresetResolver.DefaultPresetName}) has been removed. Please re-install / re-download Foreman") {
        }

        public DefaultPresetUnavailableException(string message) : base(message) {
        }

        public DefaultPresetUnavailableException(string message, Exception innerException) : base(message, innerException) {
        }
    }
}
