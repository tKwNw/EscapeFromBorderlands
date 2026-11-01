namespace EscapeFromBorderlands.Client;
// No earpiece is worn in the historical regression scenarios. New equipment is exercised in the earpieces suite.
internal static class EarpieceRuntime { internal static EFT.Player? Wearer;internal static string WornKey="";internal static bool Is(EFT.Player? p,string key)=>p!=null&&ReferenceEquals(p,Wearer)&&key==WornKey; internal static float MedicalSpeed(EFT.Player p)=>1f; }

internal static class EarpieceMedicalAnimation {internal static void Record(EFT.FirearmsAnimator animator,EFT.Player p,float native) {}}
