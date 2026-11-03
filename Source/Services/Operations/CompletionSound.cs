using System;
using System.ComponentModel;
using System.Media;

namespace IpaDesktop
{
    /// <summary>Один системный сигнал после полностью успешной очереди; звук не влияет на результат операции.</summary>
    public sealed class CompletionSound
    {
        readonly Action play;
        public CompletionSound() : this(delegate { if(RuntimePlatform.Windows)SystemSounds.Asterisk.Play(); else if(OperatingSystem.IsMacOS())System.Diagnostics.Process.Start("/usr/bin/afplay","/System/Library/Sounds/Glass.aiff"); }) { }
        public CompletionSound(Action play) { this.play = play; }

        /// <summary>Пустые, частично неудачные и отключённые уведомления остаются без звука.</summary>
        public void Notify(bool enabled, int succeeded, int failed)
        {
            if (!enabled || succeeded <= 0 || failed != 0) return;
            try { play(); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }
    }
}
