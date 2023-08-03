using StreamCompanionTypes.Enums;
using StreamCompanionTypes.Interfaces.Sources;
using StreamCompanionTypes.Interfaces;
using StreamCompanionTypes.Interfaces.Services;

namespace indicator
{
    partial class SettingsUserControl
    {
        public class IndicatorPlugin : IPlugin, ISettingsSource
        {
            public string Description => "This is EARLY/LATE in-game overlay";
            public string Name => "Indicator Plugin";
            public string Author => "C4P741N";
            public string Url => "github.com/c4p741nth";
            public string SettingGroup => "Indicator";
            private SettingsUserControl SettingsUserControl;
            public IndicatorPlugin(ILogger logger)
            {
                logger.Log("Message from { Name}!", LogLevel.Trace);
            }

            public void Free()
            {
                SettingsUserControl?.Dispose();
            }

            public object GetUiSettings()
            {
                if (SettingsUserControl == null || SettingsUserControl.IsDisposed)
                    SettingsUserControl = new SettingsUserControl();

                return SettingsUserControl;
            }
        }
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        }
        #endregion
    }
}
