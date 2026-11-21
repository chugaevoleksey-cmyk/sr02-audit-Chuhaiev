using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
var dir=Path.GetFullPath(args[0]);
var trees=Directory.GetFiles(dir,"*.cs").Order().Select(f=>CSharpSyntaxTree.ParseText(File.ReadAllText(f),path:f)).ToArray();
var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(f=>MetadataReference.CreateFromFile(f));
var compilation=CSharpCompilation.Create("Audit",trees,refs,new CSharpCompilationOptions(OutputKind.ConsoleApplication));
var types=trees.SelectMany(t=>t.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>()).Select(t=>compilation.GetSemanticModel(t.SyntaxTree).GetDeclaredSymbol(t)).ToHashSet<ISymbol>(SymbolEqualityComparer.Default);
var rows=new List<object>();var windows=new Dictionary<string,List<string>>();var inventory=new List<object>();
foreach(var tree in trees){
 var root=tree.GetRoot();var model=compilation.GetSemanticModel(tree);
 var lines=File.ReadAllLines(tree.FilePath);
 inventory.Add(new {File=Path.GetFileName(tree.FilePath),Physical=lines.Length,Logical=Logical(root).Count,Types=root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Count(),Methods=root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>().Count(),PublicMethods=root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>().Count(n=>n.Modifiers.Any(SyntaxKind.PublicKeyword))});
 foreach(var node in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>()){
  var body=(SyntaxNode)node.Body??node.ExpressionBody;if(body==null)continue;
  var cls=node.Ancestors().OfType<TypeDeclarationSyntax>().First();var self=model.GetDeclaredSymbol(cls);
  var uses=cls.DescendantNodes().OfType<TypeSyntax>().Select(n=>model.GetTypeInfo(n).Type).Where(t=>t!=null&&types.Contains(t)&&!SymbolEqualityComparer.Default.Equals(t,self)).Distinct(SymbolEqualityComparer.Default).Count();
  var branches=body.DescendantNodesAndSelf().Where(n=>n is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or CatchClauseSyntax or ConditionalExpressionSyntax or SwitchExpressionArmSyntax or CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax or ConditionalAccessExpressionSyntax || n is BinaryExpressionSyntax b&&(b.IsKind(SyntaxKind.LogicalAndExpression)||b.IsKind(SyntaxKind.LogicalOrExpression)||b.IsKind(SyntaxKind.CoalesceExpression))).Select(n=>new {Kind=n.Kind().ToString(),Line=n.GetLocation().GetLineSpan().StartLinePosition.Line+1}).ToArray();
  int depth=1+body.DescendantNodesAndSelf().Select(n=>n.AncestorsAndSelf().TakeWhile(a=>a!=body).Count(a=>a is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax or SwitchStatementSyntax or SwitchExpressionSyntax or CatchClauseSyntax or TryStatementSyntax)).DefaultIfEmpty(0).Max();
  var logical=Logical(body);var name=node is MethodDeclarationSyntax m?m.Identifier.Text:((ConstructorDeclarationSyntax)node).Identifier.Text+".ctor";
  string id=cls.Identifier.Text+"."+name;
  rows.Add(new {Class=cls.Identifier.Text,Method=name,LOC=logical.Count,CC=1+branches.Length,Depth=depth,Parameters=node.ParameterList.Parameters.Count,FanOut=uses,File=Path.GetFileName(tree.FilePath),Start=node.GetLocation().GetLineSpan().StartLinePosition.Line+1,End=node.GetLocation().GetLineSpan().EndLinePosition.Line+1,Branches=branches});
  for(int i=0;i+6<=logical.Count;i++){
   string key=string.Join("\n",logical.Skip(i).Take(6).Select(l=>l.Text));
   if(!windows.ContainsKey(key))windows[key]=new();windows[key].Add(id+":"+logical[i].Line+"-"+logical[i+5].Line);
  }
 }
}
var clones=windows.Where(w=>w.Value.Count>1).Select(w=>new {Lines=6,Locations=w.Value,Code=w.Key}).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new {Inventory=inventory,Methods=rows,CloneWindows=clones},new JsonSerializerOptions{WriteIndented=true}));
static List<(int Line,string Text)> Logical(SyntaxNode node){
 var groups=node.DescendantTokens().Where(t=>t.SpanStart>=node.SpanStart).GroupBy(t=>t.GetLocation().GetLineSpan().StartLinePosition.Line+1);
 return groups.Select(g=>(Line:g.Key,Text:string.Join(" ",g.Select(t=>t.Text)))).Where(x=>x.Text.Any(c=>!char.IsWhiteSpace(c)&&c!='{'&&c!='}')).ToList();
}
