const { chromium } = require('playwright');
(async()=>{const b=await chromium.launch();const p=await b.newPage({viewport:{width:1200,height:2000}});
await p.goto('file://'+process.cwd()+'/slides-w2.html');await p.waitForTimeout(800);
for(const id of await p.$$eval('.s',e=>e.map(x=>x.id))){await (await p.$('#'+id)).screenshot({path:'../'+id+'.png'});}
await b.close();})();
