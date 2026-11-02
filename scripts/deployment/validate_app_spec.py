import hashlib,json,pathlib,urllib.request,urllib.parse,warnings,sys
import yaml,jsonschema
warnings.filterwarnings('ignore',category=DeprecationWarning)
base='https://raw.githubusercontent.com/digitalocean/openapi/112b3762423ee6e853d7b0e9c1777fe89f493e21/specification/resources/apps/models/app_spec.yml'
cache={}
def fetch(url):
    url=urllib.parse.urldefrag(url)[0]
    if not url.startswith('https://raw.githubusercontent.com/digitalocean/openapi/'):raise RuntimeError('Unexpected schema source')
    if url not in cache:
        data=urllib.request.urlopen(url,timeout=30).read()
        cache[url]={'schema':yaml.safe_load(data),'sha256':hashlib.sha256(data).hexdigest()}
    return cache[url]['schema']
schema=fetch(base)
resolver=jsonschema.RefResolver(base_uri=base,referrer=schema,handlers={'https':fetch})
validator=jsonschema.Draft4Validator(schema,resolver=resolver)
spec=yaml.safe_load(pathlib.Path(sys.argv[1] if len(sys.argv)>1 else 'infra/app-platform/app.template.yaml').read_text())
errors=list(validator.iter_errors(spec))
for e in errors:print('/'.join(map(str,e.absolute_path)),e.message)
pathlib.Path('output/app-platform-checks').mkdir(parents=True,exist_ok=True)
pathlib.Path('output/app-platform-checks/schema-validation.json').write_text(json.dumps({'valid':not errors,'errors':[{'path':list(e.absolute_path),'message':e.message} for e in errors],'sources':{k:v['sha256'] for k,v in cache.items()}},indent=2))
print('Official OpenAPI schema files resolved:',len(cache),'errors:',len(errors))
raise SystemExit(bool(errors))
