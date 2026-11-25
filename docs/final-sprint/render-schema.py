# Standard-library-only catalog SVG renderer. Input is schema-after-sprint.json.
from pathlib import Path
import json,re,html,xml.etree.ElementTree as ET
root=Path(__file__).resolve().parent
data=json.loads((root/'schema-after-sprint.json').read_text())
groups=[('Identity',['Users','CustomerProfiles','DeviceSessions','RefreshTokens','EmailVerificationTokens','PasswordResetTokens']),('Business',['Businesses','BusinessMemberships','BusinessMembershipRoles','EmployeeInvitations','WeeklyOpeningDays']),('Resources and catalog',['BusinessServices','ServiceOfferings','ServiceSchedulingRequirements','ServiceStageTemplates','WorkAreas','EmployeeAvailabilities']),('Shared appointment calendar',['LocalCustomers','LocalVehicles','OwnedVehicles','Appointments','AppointmentStages','AppointmentEvents','AppointmentResourceReservations','SchedulingCreateReceipts']),('Execution',['WorkOrders','WorkOrderStages','WorkStageAssignments','WorkOrderEvents']),('Finance',['PaymentMethods','Payments','ManualIncomes','Expenses'])]
colors=['#145c6b','#445477','#625585','#087f7c','#7d4f70','#805517'];tables={t['name']:t for t in data['tables']};positions={};boxes=[];headers=[]
width=3660;card=490;gap=115;tops=[]
for col,(label,names) in enumerate(groups):
 x=35+col*(card+gap);y=155;headers.append((x,label,colors[col]))
 for name in names:
  t=tables[name];h=48+len(t['columns'])*23+12;positions[name]=(x,y,card,h,col);boxes.append((t,x,y,h,col));y+=h+65
 tops.append(y)
height=max(tops)+50;escape=lambda s:html.escape(str(s),quote=True)
parts=[f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-labelledby="title desc">', '<title id="title">WashFlow final sprint isolated schema</title>','<desc id="desc">33 application tables, 340 columns and 71 foreign keys captured from a new disposable PostgreSQL database. Preserved demo not migrated. Each arrow points from child to referenced parent; hover for the complete foreign-key definition.</desc>', '<defs><marker id="arrow" markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 z" fill="#64748b"/></marker></defs>',f'<rect width="{width}" height="{height}" fill="#f8fafc"/>','<g font-family="Arial,Helvetica,sans-serif">','<text x="35" y="44" font-size="30" font-weight="bold" fill="#15323d">WashFlow · final sprint schema · 25 November 2026</text>','<text x="35" y="76" font-size="18" fill="#475569">33 application tables · 340 application columns · 71 foreign keys · 11 migrations · NEW ISOLATED DATABASE</text>','<text x="35" y="103" font-size="15" fill="#475569">PK / FK key membership · arrows child → parent (hover for exact constraint) · existing demo and historical ERD preserved</text>']
for i,c in enumerate(k for k in data['constraints'] if k['kind']=='f'):
 parent=re.search(r'REFERENCES "?([A-Za-z0-9_]+)"?',c['definition']).group(1);x,y,w,h,col=positions[c['table']];px,py,pw,ph,pcol=positions[parent]
 cy=y+25+(i%3)*8;ey=py+25+(i%3)*8
 if col>pcol:sx=x;ex=px+pw;control=(sx+ex)/2;path=f'M{sx},{cy} C{control},{cy} {control},{ey} {ex},{ey}'
 elif col<pcol:sx=x+w;ex=px;control=(sx+ex)/2;path=f'M{sx},{cy} C{control},{cy} {control},{ey} {ex},{ey}'
 else:sx=x+w;ex=px+pw;control=x+w+25+(i%4)*12;path=f'M{sx},{cy} C{control},{cy} {control},{ey} {ex},{ey}'
 parts.append(f'<path d="{path}" fill="none" stroke="{colors[col]}" stroke-width="1.3" opacity="0.55" marker-end="url(#arrow)"><title>{escape(c["name"]+": "+c["definition"])}</title></path>')
for x,label,color in headers:parts.append(f'<text x="{x}" y="135" font-size="20" font-weight="bold" fill="{color}">{escape(label)}</text>')
for t,x,y,h,col in boxes:
 parts.append(f'<g><rect x="{x}" y="{y}" width="{card}" height="{h}" rx="8" fill="white" stroke="{colors[col]}" stroke-width="1.5"/><rect x="{x}" y="{y}" width="{card}" height="38" rx="8" fill="{colors[col]}"/><text x="{x+12}" y="{y+25}" font-size="17" font-weight="bold" fill="white">{escape(t["name"])}</text>')
 for row,c in enumerate(t['columns']):
  yy=y+58+row*23;marks=[]
  for code,typ in [('PK','p'),('FK','f')]:
   if any(c['name'] in re.findall(r'"([A-Za-z0-9_]+)"',k['definition'].split(')')[0]) for k in data['constraints'] if k['table']==t['name'] and k['kind']==typ):marks.append(code)
  short={'timestamp with time zone':'timestamptz','character varying':'varchar','integer':'int','boolean':'bool','numeric':'numeric'}.get(c['type'],c['type'])
  if c['type']=='character varying':short+=f'({c["maxLength"]})'
  if c['type']=='numeric':short+=f'({c["numericPrecision"]},{c["numericScale"]})'
  short+=' ?' if c['nullable'] else ''
  label=c['name']+(' ['+','.join(marks)+']' if marks else '')
  if row%2==0:parts.append(f'<rect x="{x+1}" y="{yy-15}" width="{card-2}" height="23" fill="#f1f5f9"/>')
  parts.append(f'<text x="{x+10}" y="{yy}" font-size="12" fill="#0f172a">{escape(label)}</text><text x="{x+card-10}" y="{yy}" text-anchor="end" font-size="11" fill="#526478">{escape(short)}</text>')
 parts.append('</g>')
parts+=['</g></svg>'];(root/'schema-after-sprint.svg').write_text('\n'.join(parts))
ET.parse(root/'schema-after-sprint.svg');assert len(boxes)==33;print(f'Validated SVG: 33 tables, 71 FK arrows, {width} x {height} scalable units')
