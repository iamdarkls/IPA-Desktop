using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace IpaDesktop;
/// <summary>Metadata updates notify the table without rebuilding its selection.</summary>
public sealed class AppVersionRow : INotifyPropertyChanged
{
    string id, version="—", minimumOS="—", platforms="—", state="Не запрошено";
    public string ID { get => id; set => Set(ref id,value); }
    public string Version { get => version; set => Set(ref version,value); }
    public string MinimumOS { get => minimumOS; set => Set(ref minimumOS,value); }
    public string Platforms { get => platforms; set => Set(ref platforms,value); }
    public string State { get => state; set => Set(ref state,value); }
    public event PropertyChangedEventHandler PropertyChanged;
    void Set(ref string field, string value, [CallerMemberName] string name=null)
    { if(field==value)return; field=value; PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(name)); }
}
