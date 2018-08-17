import sys
import requests

for i in range(1, len(sys.argv)):
	#print str(i) + ": " + sys.argv[i]
	url='http://ci.cuahsi.org/upload/'
	url='https://localhost:44364/Hydroserver/BulkUploadApi/'
	files={'test': open(sys.argv[i],'rb')}
	files = {'test': open('C:\CUAHSI\source\repos\QA-Azure-Hydroservertools\Test CSV Files\From ODM_ShaleNetwork_08182017\DataValues.csv', 'rb')}
	values={}
	r=requests.post(url,files=files,data=values)
	print r.status_code