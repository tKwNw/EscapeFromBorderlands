"""Small read-only ECMA-335 metadata/IL inspector; no native assembly execution."""
from pathlib import Path
import struct, re, json, sys

CODED = {
 'TypeDefOrRef': (2,[2,1,27]), 'HasConstant': (2,[4,8,23]),
 'HasCustomAttribute': (5,[6,4,1,2,8,9,10,0,14,23,20,17,26,27,32,35,38,39,40,42,44,43]),
 'HasFieldMarshal': (1,[4,8]), 'HasDeclSecurity': (2,[2,6,32]),
 'MemberRefParent': (3,[2,1,26,6,27]), 'HasSemantics': (1,[20,23]),
 'MethodDefOrRef': (1,[6,10]), 'MemberForwarded': (1,[4,6]),
 'Implementation': (2,[38,35,39]), 'CustomAttributeType': (3,[None,None,6,10,None]),
 'ResolutionScope': (2,[0,26,35,1]), 'TypeOrMethodDef': (1,[2,6])
}
SCHEMAS = {
0:[('Generation','u2'),('Name','str'),('Mvid','guid'),('EncId','guid'),('EncBaseId','guid')],
1:[('ResolutionScope','ResolutionScope'),('Name','str'),('Namespace','str')],
2:[('Flags','u4'),('Name','str'),('Namespace','str'),('Extends','TypeDefOrRef'),('FieldList',4),('MethodList',6)],
3:[('Field',4)],4:[('Flags','u2'),('Name','str'),('Signature','blob')],5:[('Method',6)],
6:[('RVA','u4'),('ImplFlags','u2'),('Flags','u2'),('Name','str'),('Signature','blob'),('ParamList',8)],
7:[('Param',8)],8:[('Flags','u2'),('Sequence','u2'),('Name','str')],
9:[('Class',2),('Interface','TypeDefOrRef')],10:[('Class','MemberRefParent'),('Name','str'),('Signature','blob')],
11:[('Type','u2'),('Parent','HasConstant'),('Value','blob')],
12:[('Parent','HasCustomAttribute'),('Type','CustomAttributeType'),('Value','blob')],
13:[('Parent','HasFieldMarshal'),('NativeType','blob')],14:[('Action','u2'),('Parent','HasDeclSecurity'),('PermissionSet','blob')],
15:[('PackingSize','u2'),('ClassSize','u4'),('Parent',2)],16:[('Offset','u4'),('Field',4)],17:[('Signature','blob')],
18:[('Parent',2),('EventList',20)],19:[('Event',20)],20:[('Flags','u2'),('Name','str'),('EventType','TypeDefOrRef')],
21:[('Parent',2),('PropertyList',23)],22:[('Property',23)],23:[('Flags','u2'),('Name','str'),('Type','blob')],
24:[('Semantics','u2'),('Method',6),('Association','HasSemantics')],25:[('Class',2),('MethodBody','MethodDefOrRef'),('MethodDeclaration','MethodDefOrRef')],
26:[('Name','str')],27:[('Signature','blob')],28:[('MappingFlags','u2'),('MemberForwarded','MemberForwarded'),('ImportName','str'),('ImportScope',26)],
29:[('RVA','u4'),('Field',4)],30:[('Token','u4'),('FuncCode','u4')],31:[('Token','u4')],
32:[('HashAlgId','u4'),('MajorVersion','u2'),('MinorVersion','u2'),('BuildNumber','u2'),('RevisionNumber','u2'),('Flags','u4'),('PublicKey','blob'),('Name','str'),('Culture','str')],
33:[('Processor','u4')],34:[('OSPlatformID','u4'),('OSMajorVersion','u4'),('OSMinorVersion','u4')],
35:[('MajorVersion','u2'),('MinorVersion','u2'),('BuildNumber','u2'),('RevisionNumber','u2'),('Flags','u4'),('PublicKeyOrToken','blob'),('Name','str'),('Culture','str'),('HashValue','blob')],
36:[('Processor','u4'),('AssemblyRef',35)],37:[('OSPlatformID','u4'),('OSMajorVersion','u4'),('OSMinorVersion','u4'),('AssemblyRef',35)],
38:[('Flags','u4'),('Name','str'),('HashValue','blob')],39:[('Flags','u4'),('TypeDefId','u4'),('TypeName','str'),('TypeNamespace','str'),('Implementation','Implementation')],
40:[('Offset','u4'),('Flags','u4'),('Name','str'),('Implementation','Implementation')],41:[('NestedClass',2),('EnclosingClass',2)],
42:[('Number','u2'),('Flags','u2'),('Owner','TypeOrMethodDef'),('Name','str')],43:[('Method','MethodDefOrRef'),('Instantiation','blob')],44:[('Owner',42),('Constraint','TypeDefOrRef')]
}
ELEMENTS={1:'void',2:'bool',3:'char',4:'sbyte',5:'byte',6:'short',7:'ushort',8:'int',9:'uint',10:'long',11:'ulong',12:'float',13:'double',14:'string',22:'TypedReference',24:'IntPtr',25:'UIntPtr',28:'object'}
OP_NAMES={0:'nop',1:'break',2:'ldarg.0',3:'ldarg.1',4:'ldarg.2',5:'ldarg.3',6:'ldloc.0',7:'ldloc.1',8:'ldloc.2',9:'ldloc.3',10:'stloc.0',11:'stloc.1',12:'stloc.2',13:'stloc.3',14:'ldarg.s',15:'ldarga.s',16:'starg.s',17:'ldloc.s',18:'ldloca.s',19:'stloc.s',20:'ldnull',21:'ldc.i4.m1',31:'ldc.i4.s',32:'ldc.i4',33:'ldc.i8',34:'ldc.r4',35:'ldc.r8',37:'dup',38:'pop',39:'jmp',40:'call',41:'calli',42:'ret',43:'br.s',44:'brfalse.s',45:'brtrue.s',46:'beq.s',47:'bge.s',48:'bgt.s',49:'ble.s',50:'blt.s',51:'bne.un.s',52:'bge.un.s',53:'bgt.un.s',54:'ble.un.s',55:'blt.un.s',56:'br',57:'brfalse',58:'brtrue',59:'beq',60:'bge',61:'bgt',62:'ble',63:'blt',64:'bne.un',65:'bge.un',66:'bgt.un',67:'ble.un',68:'blt.un',69:'switch',88:'add',89:'sub',90:'mul',91:'div',92:'div.un',93:'rem',94:'rem.un',95:'and',96:'or',97:'xor',98:'shl',99:'shr',100:'shr.un',101:'neg',102:'not',111:'callvirt',112:'cpobj',113:'ldobj',114:'ldstr',115:'newobj',116:'castclass',117:'isinst',121:'unbox',122:'throw',123:'ldfld',124:'ldflda',125:'stfld',126:'ldsfld',127:'ldsflda',128:'stsfld',129:'stobj',140:'box',141:'newarr',142:'ldlen',143:'ldelema',163:'ldelem',164:'stelem',165:'unbox.any',194:'refanyval',198:'mkrefany',208:'ldtoken',220:'endfinally',221:'leave',222:'leave.s',0xfe01:'ceq',0xfe02:'cgt',0xfe03:'cgt.un',0xfe04:'clt',0xfe05:'clt.un',0xfe06:'ldftn',0xfe07:'ldvirtftn',0xfe09:'ldarg',0xfe0a:'ldarga',0xfe0b:'starg',0xfe0c:'ldloc',0xfe0d:'ldloca',0xfe0e:'stloc',0xfe15:'initobj',0xfe16:'constrained',0xfe1c:'sizeof'}
for i in range(22,31): OP_NAMES[i]='ldc.i4.'+str(i-22)
TOKEN_OPS={39,40,41,111,112,113,114,115,116,117,121,123,124,125,126,127,128,129,140,141,143,163,164,165,194,198,208,0xfe06,0xfe07,0xfe15,0xfe16,0xfe1c}
SHORT_OPS=set(range(14,20))|{31}|set(range(43,56))|{222,0xfe12}
LONG_OPS={32}|set(range(56,69))|{221}

class Assembly:
 def __init__(self,path):
  self.path=Path(path);self.data=self.path.read_bytes();d=self.data
  pe=self.u4(0x3c);sections=self.u2(pe+6);opts=self.u2(pe+20);opt=pe+24
  self.sections=[]
  for i in range(sections):
   p=opt+opts+40*i
   self.sections.append((self.u4(p+12),self.u4(p+8),self.u4(p+20),self.u4(p+16)))
  dd=opt+(112 if self.u2(opt)==0x20b else 96)
  cli=self.offset(self.u4(dd+14*8));root=self.offset(self.u4(cli+8))
  if d[root:root+4]!=b'BSJB':raise ValueError('Not CLI metadata')
  p=(root+16+self.u4(root+12)+3)&~3;count=self.u2(p+2);p+=4;self.streams={}
  for i in range(count):
   off,size=self.u4(p),self.u4(p+4);end=d.index(0,p+8);name=d[p+8:end].decode();p=(end+4)&~3
   self.streams[name]=(root+off,size)
  self.strings=self.streams['#Strings'][0];self.blobs=self.streams['#Blob'][0]
  p=self.streams.get('#~',self.streams.get('#-'))[0];self.heaps=d[p+6];valid=self.u8(p+8);p+=24;self.counts={}
  for t in range(64):
   if valid>>t&1:self.counts[t]=self.u4(p);p+=4
  self.tables={}
  for t,n in self.counts.items():
   schema=SCHEMAS[t];rows=[None]
   for i in range(n):
    row={}
    for name,kind in schema:
     width=self.width(kind);value=int.from_bytes(d[p:p+width],'little');p+=width
     if kind=='str':value=self.string(value)
     row[name]=value
    rows.append(row)
   self.tables[t]=rows
  self.nested={r['NestedClass']:r['EnclosingClass'] for r in self.rows(41)}
  self.method_owners={};self.field_owners={}
  for i,row in enumerate(self.rows(2),1):
   for table,key,owners in [(6,'MethodList',self.method_owners),(4,'FieldList',self.field_owners)]:
    end=self.tables[2][i+1][key] if i+1<len(self.tables[2]) else self.counts.get(table,0)+1
    for j in range(row[key],end):owners[j]=i
 def u2(self,p):return struct.unpack_from('<H',self.data,p)[0]
 def u4(self,p):return struct.unpack_from('<I',self.data,p)[0]
 def u8(self,p):return struct.unpack_from('<Q',self.data,p)[0]
 def offset(self,rva):
  for va,vs,raw,rs in self.sections:
   if va<=rva<va+max(vs,rs):return raw+rva-va
  raise ValueError(f'Unmapped RVA {rva:x}')
 def width(self,kind):
  if kind in ('u2','u4'):return int(kind[1:])
  if kind in ('str','guid','blob'):return 4 if self.heaps&{'str':1,'guid':2,'blob':4}[kind] else 2
  if isinstance(kind,int):return 4 if self.counts.get(kind,0)>=65536 else 2
  bits,tables=CODED[kind];return 4 if any(self.counts.get(t,0)>=1<<(16-bits) for t in tables if t is not None) else 2
 def string(self,index):
  p=self.strings+index;return self.data[p:self.data.index(0,p)].decode('utf-8',errors='replace')
 @staticmethod
 def compressed(data,p):
  b=data[p]
  if b<128:return b,p+1
  if b<192:return ((b&63)<<8)|data[p+1],p+2
  return ((b&31)<<24)|(data[p+1]<<16)|(data[p+2]<<8)|data[p+3],p+4
 def blob(self,index):
  if not index:return b''
  size,p=self.compressed(self.data,self.blobs+index);return self.data[p:p+size]
 def rows(self,table):return self.tables.get(table,[None])[1:]
 def coded(self,name,value):
  bits,tables=CODED[name];tag=value&((1<<bits)-1);return tables[tag],value>>bits
 def typename(self,table,index):
  if not index:return 'null'
  row=self.tables[table][index]
  if table==27:return self.type_sig(self.blob(row['Signature']),0)[0]
  if table==2 and index in self.nested:return self.typename(2,self.nested[index])+'+'+row['Name']
  if table==1:
   scope,number=self.coded('ResolutionScope',row['ResolutionScope'])
   if scope==1:return self.typename(1,number)+'+'+row['Name']
  return (row.get('Namespace','')+'.'+row['Name']).strip('.')
 def type_sig(self,data,p):
  t=data[p];p+=1
  if t in ELEMENTS:return ELEMENTS[t],p
  if t in (17,18):
   token,p=self.compressed(data,p);table,index=self.coded('TypeDefOrRef',token);return self.typename(table,index),p
  if t in (15,16,29,69):
   name,p=self.type_sig(data,p);return name+{15:'*',16:'&',29:'[]',69:' pinned'}[t],p
  if t in (19,30):
   index,p=self.compressed(data,p);return ('!' if t==19 else '!!')+str(index),p
  if t in (31,32):
   token,p=self.compressed(data,p);return self.type_sig(data,p)
  if t==21:
   name,p=self.type_sig(data,p);count,p=self.compressed(data,p);args=[]
   for i in range(count):arg,p=self.type_sig(data,p);args.append(arg)
   return name+'<'+','.join(args)+'>',p
  if t==20:
   name,p=self.type_sig(data,p);rank,p=self.compressed(data,p);n,p=self.compressed(data,p)
   for i in range(n):_,p=self.compressed(data,p)
   n,p=self.compressed(data,p)
   for i in range(n):_,p=self.compressed(data,p)
   return name+'['+','*max(0,rank-1)+']',p
  if t==27:
   name,p=self.method_sig(data,p);return 'fnptr '+name,p
  if t==65:return self.type_sig(data,p)
  raise ValueError(f'Unknown element {t:x} at {p-1}: {data.hex()}')
 def method_sig(self,data,p=0):
  flags=data[p];p+=1
  if flags&16:_,p=self.compressed(data,p)
  count,p=self.compressed(data,p);ret,p=self.type_sig(data,p);args=[]
  for i in range(count):name,p=self.type_sig(data,p);args.append(name)
  return ret+' ('+', '.join(args)+')',p
 def signature(self,table,index):
  row=self.tables[table][index];data=self.blob(row.get('Signature',row.get('Type',0)))
  if not data:return ''
  if data[0]==6:return self.type_sig(data,1)[0]
  if data[0]==10:
   n,p=self.compressed(data,1);args=[]
   for i in range(n):arg,p=self.type_sig(data,p);args.append(arg)
   return '<'+','.join(args)+'>'
  return self.method_sig(data)[0]
 def token(self,value):
  table,index=value>>24,value&0xffffff
  if table==0x70:
   p=self.streams['#US'][0]+index;n,p=self.compressed(self.data,p);return repr(self.data[p:p+n-1].decode('utf-16-le',errors='replace'))
  if table in (1,2,27):return self.typename(table,index)
  row=self.tables.get(table,[None])[index]
  if table in (4,6):owner=self.typename(2,(self.field_owners if table==4 else self.method_owners)[index])
  elif table==10:
   pt,pi=self.coded('MemberRefParent',row['Class']);owner=self.typename(pt,pi) if pt in (1,2,27) else str((pt,pi))
  elif table==43:
   mt,mi=self.coded('MethodDefOrRef',row['Method']);return self.token((mt<<24)|mi)+self.signature(table,index)
  else:return f'{value:08x}'
  return owner+'::'+row['Name']+' '+self.signature(table,index)
 def members(self,type_index,table):
  key={4:'FieldList',6:'MethodList'}[table];row=self.tables[2][type_index]
  end=self.tables[2][type_index+1][key] if type_index+1<len(self.tables[2]) else self.counts.get(table,0)+1
  return [(i,self.tables[table][i]) for i in range(row[key],end)]
 def params(self,index):
  row=self.tables[6][index];end=self.tables[6][index+1]['ParamList'] if index+1<len(self.tables[6]) else self.counts.get(8,0)+1
  return [(self.tables[8][i]['Sequence'],self.tables[8][i]['Name']) for i in range(row['ParamList'],end)]
 def il(self,index):
  rva=self.tables[6][index]['RVA']
  if not rva:return []
  p=self.offset(rva);first=self.data[p]
  if first&3==2:size=first>>2;p+=1
  else:size=self.u4(p+4);p+=((self.u2(p)>>12)&15)*4
  end=p+size;start=p;out=[]
  while p<end:
   at=p-start;code=self.data[p];p+=1
   if code==254:code=(code<<8)|self.data[p];p+=1
   value=''
   if code in TOKEN_OPS:
    raw=self.u4(p);p+=4
    try:value=self.token(raw)
    except Exception:value=f'token {raw:08x}'
   elif code in SHORT_OPS:value=str(struct.unpack_from('<b',self.data,p)[0]);p+=1
   elif code in LONG_OPS:value=str(struct.unpack_from('<i',self.data,p)[0]);p+=4
   elif code in (33,34,35):
    form={33:'q',34:'f',35:'d'}[code];value=str(struct.unpack_from('<'+form,self.data,p)[0]);p+=struct.calcsize(form)
   elif code==69:
    n=self.u4(p);p+=4;value=str([struct.unpack_from('<i',self.data,p+4*i)[0] for i in range(n)]);p+=4*n
   elif code in range(0xfe09,0xfe0f):value=str(self.u2(p));p+=2
   out.append(f'{at:04x} {OP_NAMES.get(code,f"op_{code:04x}")} {value}'.rstrip())
  return out
 def find(self,pattern):return [(i,self.typename(2,i)) for i in range(1,len(self.tables[2])) if re.search(pattern,self.typename(2,i))]
 def dump(self,index,methods=None,with_il=False):
  row=self.tables[2][index];parent=self.coded('TypeDefOrRef',row['Extends']);out=[self.typename(2,index)+' : '+self.typename(*parent)]
  for i,f in self.members(index,4):out.append(f" FIELD {f['Flags']:04x} {f['Name']}: {self.signature(4,i)}")
  for i,m in self.members(index,6):
   if methods is not None and not re.search(methods,m['Name']):continue
   out.append(f" METHOD {i:06x} {m['Flags']:04x} {m['Name']}: {self.signature(6,i)} params={self.params(i)}")
   if with_il:out.extend('  '+line for line in self.il(i))
  return '\n'.join(out)

if __name__=='__main__':
 a=Assembly(sys.argv[1]);pattern=sys.argv[2] if len(sys.argv)>2 else '.*'
 if '--names' in sys.argv:
  for i,name in a.find(pattern):print(i,name)
 else:
  method=sys.argv[3] if len(sys.argv)>3 and not sys.argv[3].startswith('--') else None
  for i,name in a.find(pattern):print(a.dump(i,method,'--il' in sys.argv))
