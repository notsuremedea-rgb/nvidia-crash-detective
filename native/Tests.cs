using System;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
namespace NvidiaCrashDetective {
class Tests {
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 [STAThread] static void Main(){
  Sample s=new Sample();s.Values["clocks.current.graphics"]="292";s.Values["temperature.gpu"]="40";
  Check(Telemetry.Warning(s)=="","ordinary idle sample accepted");
  s.Values["clocks.current.graphics"]="47549";Check(Telemetry.Warning(s).Contains("clock"),"impossible clock flagged");
  s.Values["clocks.current.graphics"]="N/A";Check(!s.Number("clocks.current.graphics").HasValue,"missing sensor is not zero");
  s.Error="Telemetry query timed out";Check(Telemetry.Warning(s)==s.Error,"query error retained");
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var window=new MainWindow();var guard=typeof(MainWindow).GetMethod("GuardLogging",flags);
  guard.Invoke(window,new object[]{new Action(()=>{throw new IOException("test disk failure");})});
  Check(((TextBlock)typeof(MainWindow).GetField("status",flags).GetValue(window)).Text.Contains("LOG SAVE FAILED"),"disk error visibly reported");
  guard.Invoke(window,new object[]{new Action(()=>{throw new UnauthorizedAccessException("test denied");})});
  Check(typeof(MainWindow).GetField("log",flags).GetValue(window)==null,"access failure leaves recording stopped");
  typeof(MainWindow).GetMethod("Consume",flags).Invoke(window,new object[]{new Sample()});
  Check(((TextBlock)typeof(MainWindow).GetField("status",flags).GetValue(window)).Text.Contains("LOG SAVE FAILED"),"subsequent telemetry does not hide logging failure");
  window.Close();Console.WriteLine(count+" checks passed; no GPU queries or settings changes.");
 }
}
}
