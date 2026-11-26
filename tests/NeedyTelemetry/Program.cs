using System;
static class Test {
 static void Check(bool v,string label){if(!v)throw new Exception(label);}
 static void Main(){
  var t=new NeedyCycleTelemetry();
  t.Observe(1);t.Opportunity(2);t.Observe(2);t.Observe(3);
  Check(t.ActivationCount==1 && t.SuccessCount==1 && t.StrikeCount==0,"correct response");
  t.Opportunity(2);t.Strike();t.Observe(3);
  Check(t.ActivationCount==2 && t.SuccessCount==1 && t.StrikeCount==1,"wrong response");
  t.Opportunity(2);t.Strike();t.Observe(3);
  Check(t.ActivationCount==3 && t.SuccessCount==1 && t.StrikeCount==2,"timeout");
  t.Opportunity(2);t.End(3);t.Strike();t.FinalizeEnd();
  Check(t.ActivationCount==4 && t.SuccessCount==1 && t.StrikeCount==3 && t.ActiveAtEnd==false && t.UnresolvedCount==0,"final strike ordering");
  var lateStrike=new NeedyCycleTelemetry();lateStrike.Opportunity(2);lateStrike.Observe(3);lateStrike.Strike();
  Check(!lateStrike.CountsReliable && lateStrike.StrikeCount==1,"strike after cooldown retracts success");
  var active=new NeedyCycleTelemetry();active.Opportunity(2);active.End(2);
  Check(!active.TerminalReconciled && active.ActiveAtEnd==null,"immediate terminal snapshot is not reconciled");
  active.FinalizeEnd();
  Check(active.TerminalReconciled,"terminal snapshot becomes reconciled");
  Check(active.ActiveAtEnd==true && active.SuccessCount==0,"bomb ends while active");
  var resolved=new NeedyCycleTelemetry();resolved.Opportunity(2);resolved.End(3);resolved.FinalizeEnd();
  Check(resolved.SuccessCount==1 && resolved.ActiveAtEnd==false,"correct response at end");
  var unknown=new NeedyCycleTelemetry();unknown.Opportunity(2);unknown.End(5);unknown.FinalizeEnd();
  Check(unknown.SuccessCount==0 && unknown.ActiveAtEnd==null && unknown.UnresolvedCount==1,"ambiguous terminal ordering");
  var unseen=new NeedyCycleTelemetry();unseen.End(5);unseen.FinalizeEnd();
  Check(unseen.ActiveAtEnd==false && unseen.UnresolvedCount==0 && unseen.ActivationCount==0,"unknown end must not invent a cycle");
  var priorStrike=new NeedyCycleTelemetry();priorStrike.Opportunity(2);priorStrike.Strike();priorStrike.Observe(3);priorStrike.End(5);priorStrike.FinalizeEnd();
  Check(priorStrike.ActiveAtEnd==false && priorStrike.UnresolvedCount==0 && priorStrike.StrikeCount==1,"previous strike does not settle a later ambiguous ending");
  var clean=new NeedyCycleTelemetry();clean.Observe(1);clean.End(1);clean.FinalizeEnd();
  Check(clean.ActivationCount==0 && clean.UnresolvedCount==0 && clean.ActiveAtEnd==false,"clean end without activation");
  var sameFrame=new NeedyCycleTelemetry();sameFrame.End(2);sameFrame.FinalizeEnd();
  Check(sameFrame.ActivationCount==0 && sameFrame.ActiveAtEnd==false,"terminal-only activation is not actionable");
  var priorSuccess=new NeedyCycleTelemetry();priorSuccess.Opportunity(2);priorSuccess.Observe(3);priorSuccess.Opportunity(2);priorSuccess.End(5);priorSuccess.FinalizeEnd();
  Check(priorSuccess.ActivationCount==2 && priorSuccess.SuccessCount==1,"ambiguous terminal state preserves earlier counts");
  var unseenFinal=new NeedyCycleTelemetry();unseenFinal.Observe(2);unseenFinal.End(2);unseenFinal.FinalizeEnd();
  Check(unseenFinal.ActivationCount==0 && unseenFinal.ActiveAtEnd==false,"final cycle without opportunity ignored");
  var unseenStrike=new NeedyCycleTelemetry();unseenStrike.Observe(2);unseenStrike.Strike();unseenStrike.End(3);unseenStrike.FinalizeEnd();
  Check(unseenStrike.ActivationCount==1 && unseenStrike.StrikeCount==1,"unseen cycle causing strike counts");
  var transient=new NeedyCycleTelemetry();transient.Observe(2);transient.Observe(3);transient.End(1);transient.FinalizeEnd();
  Check(transient.ActivationCount==0 && transient.SuccessCount==0,"unseen harmless cycle ignored");
  var strikeAfterCooldown=new NeedyCycleTelemetry();strikeAfterCooldown.Observe(2);strikeAfterCooldown.Observe(3);strikeAfterCooldown.Strike();
  Check(!strikeAfterCooldown.CountsReliable && strikeAfterCooldown.StrikeCount==1,"delayed strike qualifies unseen cycle");
  var orphan=new NeedyCycleTelemetry();orphan.Strike();
  Check(!orphan.CountsReliable && orphan.StrikeCount==1,"orphan strike leaves cycle counts unknown");
  Console.WriteLine("telemetry tests passed");
 }
}
