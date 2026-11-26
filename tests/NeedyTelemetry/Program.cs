using System;
static class Test {
 static void Check(bool v,string label){if(!v)throw new Exception(label);}
 static void Main(){
  var t=new NeedyCycleTelemetry();
  t.Observe(1);t.Observe(2);t.Observe(2);t.Observe(3);
  Check(t.ActivationCount==1 && t.SuccessCount==1 && t.StrikeCount==0,"correct response");
  t.Observe(2);t.Strike();t.Observe(3);
  Check(t.ActivationCount==2 && t.SuccessCount==1 && t.StrikeCount==1,"wrong response");
  t.Observe(2);t.Strike();t.Observe(3);
  Check(t.ActivationCount==3 && t.SuccessCount==1 && t.StrikeCount==2,"timeout");
  t.Observe(2);t.End(3);t.Strike();t.FinalizeEnd();
  Check(t.ActivationCount==4 && t.SuccessCount==1 && t.StrikeCount==3 && t.ActiveAtEnd==false && t.UnresolvedCount==0,"final strike ordering");
  var lateStrike=new NeedyCycleTelemetry();lateStrike.Observe(2);lateStrike.Observe(3);lateStrike.Strike();
  Check(lateStrike.SuccessCount==0 && lateStrike.StrikeCount==1,"strike after cooldown retracts success");
  var active=new NeedyCycleTelemetry();active.Observe(2);active.End(2);
  Check(!active.TerminalReconciled && active.ActiveAtEnd==null,"immediate terminal snapshot is not reconciled");
  active.FinalizeEnd();
  Check(active.TerminalReconciled,"terminal snapshot becomes reconciled");
  Check(active.ActiveAtEnd==true && active.SuccessCount==0,"bomb ends while active");
  var resolved=new NeedyCycleTelemetry();resolved.Observe(2);resolved.End(3);resolved.FinalizeEnd();
  Check(resolved.SuccessCount==1 && resolved.ActiveAtEnd==false,"correct response at end");
  var unknown=new NeedyCycleTelemetry();unknown.Observe(2);unknown.End(5);unknown.FinalizeEnd();
  Check(unknown.SuccessCount==0 && unknown.ActiveAtEnd==null && unknown.UnresolvedCount==1,"ambiguous terminal ordering");
  var unseen=new NeedyCycleTelemetry();unseen.End(5);unseen.FinalizeEnd();
  Check(unseen.ActiveAtEnd==null && unseen.UnresolvedCount==0 && unseen.ActivationCount==0,"unknown end must not invent a cycle");
  var priorStrike=new NeedyCycleTelemetry();priorStrike.Observe(2);priorStrike.Strike();priorStrike.Observe(3);priorStrike.End(5);priorStrike.FinalizeEnd();
  Check(priorStrike.ActiveAtEnd==null && priorStrike.UnresolvedCount==0 && priorStrike.StrikeCount==1,"previous strike does not settle a later ambiguous ending");
  var clean=new NeedyCycleTelemetry();clean.Observe(1);clean.End(1);clean.FinalizeEnd();
  Check(clean.ActivationCount==0 && clean.UnresolvedCount==0 && clean.ActiveAtEnd==false,"clean end without activation");
  var sameFrame=new NeedyCycleTelemetry();sameFrame.End(2);sameFrame.FinalizeEnd();
  Check(sameFrame.ActivationCount==1 && sameFrame.ActiveAtEnd==true,"activation observed at end");
  Console.WriteLine("telemetry tests passed");
 }
}
