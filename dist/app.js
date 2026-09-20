const bands = [
  {name:'Sub foundation', role:'Room / weight', hz:'32 Hz', value:0},
  {name:'Kick body', role:'Punch / impact', hz:'64 Hz', value:0},
  {name:'Low-mid', role:'Warmth / mud', hz:'250 Hz', value:0},
  {name:'Presence', role:'Voice / attack', hz:'2.5 kHz', value:0},
  {name:'Detail', role:'Clarity / bite', hz:'6.4 kHz', value:0},
  {name:'Air', role:'Space / shimmer', hz:'12 kHz', value:0}
];
const presets = {
  reference:[0,0,0,0,0,0], impact:[4,3,-1.5,1,1.5,.5], dialogue:[-2,-1,-2,2.5,1.5,0], night:[-3,-2,1,1,-1,-2]
};
const grid = document.querySelector('#bandGrid');
const curveLine = document.querySelector('#curveLine');
const curveFill = document.querySelector('#curveFill');
const curveReadout = document.querySelector('#curveReadout');
const preamp = document.querySelector('#preamp');
const limiter = document.querySelector('#limiter');
const outputMode = document.querySelector('#outputMode');
const headphoneDeck = document.querySelector('#headphoneDeck');
const headphoneTarget = document.querySelector('#headphoneTarget');
const headphoneSoftware = document.querySelector('#headphoneSoftware');
const crossfeed = document.querySelector('#crossfeed');
const stageWidth = document.querySelector('#stageWidth');
const profileFile = document.querySelector('#profileFile');
const profileList = document.querySelector('#profileList');
const profileStatus = document.querySelector('#profileStatus');
const savedProfileList = document.querySelector('#savedProfileList');
const profileName = document.querySelector('#profileName');
const saveProfile = document.querySelector('#saveProfile');
const saveStatus = document.querySelector('#saveStatus');
const measurementInspector = document.querySelector('#measurementInspector');
const measurementMode = document.querySelector('#measurementMode');
const measurementName = document.querySelector('#measurementName');
const measurementMeta = document.querySelector('#measurementMeta');
const rawProfileLine = document.querySelector('#rawProfileLine');
const targetProfileLine = document.querySelector('#targetProfileLine');
const correctionProfileLine = document.querySelector('#correctionProfileLine');
const measurementRawPeak = document.querySelector('#measurementRawPeak');
const measurementCorrectionPeak = document.querySelector('#measurementCorrectionPeak');
const measurementPreamp = document.querySelector('#measurementPreamp');
const applyMeasurement = document.querySelector('#applyMeasurement');
const exportMeasurement = document.querySelector('#exportMeasurement');
const measurementAudit = document.querySelector('#measurementAudit');
const profileStorageKey = 'geteqd-imported-profiles-v1';
const savedProfileStorageKey = 'geteqd-listening-profiles-v1';
const targetFrequencies = [32,64,250,2500,6400,12000];
let selectedMeasurement = null;

function safeProfiles(){
  try { const profiles=JSON.parse(localStorage.getItem(profileStorageKey) || '[]'); return Array.isArray(profiles) ? profiles.map(profile=>({...profile,responseType:profile.responseType==='raw'?'raw':'correction',rig:typeof profile.rig==='string'?profile.rig:'',target:typeof profile.target==='string'?profile.target:'',targetDb:Array.isArray(profile.targetDb)?profile.targetDb:(Array.isArray(profile.frequenciesHz)?profile.frequenciesHz.map(()=>0):[])})) : []; }
  catch { return []; }
}
function formatDate(value){
  if(!value) return 'Date not supplied';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString(undefined,{year:'numeric',month:'short',day:'numeric'});
}
function escapeHtml(value){ return String(value).replace(/[&<>\"']/g,character=>({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;',"'":'&#39;'}[character])); }
function safeSavedProfiles(){
  try { const profiles=JSON.parse(localStorage.getItem(savedProfileStorageKey) || '[]'); return Array.isArray(profiles) ? profiles.map(profile=>({name:typeof profile?.name==='string'?profile.name:'Untitled profile',savedAt:profile?.savedAt||'',settings:{bands:Array.isArray(profile?.settings?.bands)&&profile.settings.bands.length===bands.length?profile.settings.bands.map(Number):bands.map(band=>band.value),preamp:Number(profile?.settings?.preamp)||0,limiter:Number(profile?.settings?.limiter)||0,outputMode:profile?.settings?.outputMode||outputMode.options[0].value,headphoneTarget:profile?.settings?.headphoneTarget||headphoneTarget.options[0].value,headphoneSoftware:profile?.settings?.headphoneSoftware||headphoneSoftware.options[0].value,crossfeed:Number(profile?.settings?.crossfeed)||0,stageWidth:Number(profile?.settings?.stageWidth)||100,bypassed:!!profile?.settings?.bypassed}})) : []; }
  catch { return []; }
}
function markUnsaved(){ saveStatus.textContent='Live changes not saved yet.'; }
function validateProfile(candidate){
  if(!candidate || candidate.schema !== 'getEQd-profile/v1') throw new Error('Schema must be getEQd-profile/v1.');
  if(typeof candidate.model !== 'string' || !candidate.model.trim()) throw new Error('Add a model name.');
  if(!Array.isArray(candidate.frequenciesHz) || !Array.isArray(candidate.gainDb) || candidate.frequenciesHz.length !== candidate.gainDb.length || candidate.frequenciesHz.length < 2) throw new Error('Frequencies and gainDb must be matching arrays with at least two points.');
  if(candidate.frequenciesHz.some((value,i)=>!Number.isFinite(value) || value <= 0 || !Number.isFinite(candidate.gainDb[i]))) throw new Error('Frequency and gain values must be finite numbers.');
  const responseType = typeof candidate.responseType === 'string' && candidate.responseType.trim() ? candidate.responseType.trim().toLowerCase() : 'correction';
  if(responseType !== 'raw' && responseType !== 'correction') throw new Error('responseType must be raw or correction.');
  const targetDb = candidate.targetDb === undefined ? candidate.frequenciesHz.map(()=>0) : candidate.targetDb;
  if(!Array.isArray(targetDb) || targetDb.length !== candidate.frequenciesHz.length || targetDb.some(value=>!Number.isFinite(value))) throw new Error('targetDb must be an array matching frequenciesHz.');
  return {schema:'getEQd-profile/v1',model:candidate.model.trim(),source:typeof candidate.source === 'string' && candidate.source.trim() ? candidate.source.trim() : 'Local import',measuredAt:candidate.measuredAt || '',rig:typeof candidate.rig === 'string' ? candidate.rig.trim() : '',target:typeof candidate.target === 'string' ? candidate.target.trim() : '',responseType,notes:typeof candidate.notes === 'string' ? candidate.notes.trim() : '',frequenciesHz:candidate.frequenciesHz.map(Number),gainDb:candidate.gainDb.map(Number),targetDb:targetDb.map(Number)};
}
function interpolateValues(frequencies, values, frequency){
  const points = frequencies.map((hz,i)=>({hz,gain:values[i]})).sort((a,b)=>a.hz-b.hz);
  if(frequency <= points[0].hz) return points[0].gain;
  if(frequency >= points[points.length-1].hz) return points[points.length-1].gain;
  const upper = points.findIndex(point=>point.hz >= frequency); const low = points[upper-1]; const high = points[upper];
  const ratio = (Math.log(frequency)-Math.log(low.hz))/(Math.log(high.hz)-Math.log(low.hz));
  return low.gain + (high.gain-low.gain)*ratio;
}
function rawAt(profile,frequency){return interpolateValues(profile.frequenciesHz,profile.gainDb,frequency);}
function targetAt(profile,frequency){return interpolateValues(profile.frequenciesHz,profile.targetDb,frequency);}
function correctionAt(profile,frequency){const raw=rawAt(profile,frequency);return profile.responseType==='raw' ? targetAt(profile,frequency)-raw : raw;}
function applyProfile(profile){
  selectedMeasurement=profile; renderMeasurementInspector();
  targetFrequencies.forEach((frequency,index)=>{bands[index].value=Math.max(-12,Math.min(11,Math.round(correctionAt(profile,frequency)*2)/2));});
  document.querySelectorAll('.preset').forEach(button=>button.classList.remove('active')); renderBands(); sync(); document.querySelector('#console').scrollIntoView({behavior:'smooth',block:'start'});
  profileStatus.textContent = `${profile.model} correction applied to the six-band preview. Original measurement remains unchanged.`;
}
function renderProfiles(){
  const profiles=safeProfiles();
  profileList.innerHTML = profiles.length ? profiles.map((profile,index)=>`<article class="profile-card"><div class="profile-card-top"><span class="version-chip">LOCAL · ${profile.responseType==='raw'?'RAW MEASURED':'CORRECTION'}</span><span class="profile-count">${profile.frequenciesHz.length} points</span></div><h3>${escapeHtml(profile.model)}</h3><p>${escapeHtml(profile.source)} · ${formatDate(profile.measuredAt)}</p><p>${escapeHtml(profile.rig||'Rig not supplied')} · ${escapeHtml(profile.target||'Flat target')}</p>${profile.notes?`<p class="profile-notes">${escapeHtml(profile.notes)}</p>`:''}<button class="profile-apply" type="button" data-profile-index="${index}">Open in model lab <span>→</span></button><button class="profile-apply" type="button" data-audition-index="${index}">Audition correction <span>→</span></button></article>`).join('') : '<div class="profile-empty"><strong>Your measured profiles will live here.</strong><p>Import a JSON measurement to create the first local profile. No account or upload is involved.</p></div>';
  profileList.querySelectorAll('[data-profile-index]').forEach(button=>button.addEventListener('click',()=>{selectedMeasurement=profiles[+button.dataset.profileIndex];renderMeasurementInspector();}));
  profileList.querySelectorAll('[data-audition-index]').forEach(button=>button.addEventListener('click',()=>applyProfile(profiles[+button.dataset.auditionIndex])));
}
function profilePeak(profile,correction=false){
  let peak=-Infinity;
  for(let index=0;index<=240;index++){
    const frequency=20*Math.pow(1000,index/240);
    const value=correction?correctionAt(profile,frequency):rawAt(profile,frequency);
    if(value>peak) peak=value;
  }
  return Number.isFinite(peak)?peak:0;
}
function dbLabel(value){return `${value>0?'+':''}${value.toFixed(1)} dB`;}
function measurementPath(profile,mode){
  const points=[];
  for(let x=0;x<=1000;x+=10){
    const frequency=20*Math.pow(1000,x/1000);
    const value=mode==='raw'?rawAt(profile,frequency):mode==='target'?targetAt(profile,frequency):correctionAt(profile,frequency);
    const y=Math.max(8,Math.min(252,130-(value/15)*108));
    points.push(`${x.toFixed(1)},${y.toFixed(1)}`);
  }
  return `M ${points.join(' L ')}`;
}
function renderMeasurementInspector(){
  if(!selectedMeasurement){measurementInspector.hidden=true;return;}
  const profile=selectedMeasurement;
  const rawPeak=profilePeak(profile); const correctionPeak=profilePeak(profile,true);
  measurementInspector.hidden=false;
  measurementMode.textContent=profile.responseType==='raw'?'RAW MEASUREMENT':'CORRECTION DATA';
  measurementName.textContent=profile.model;
  measurementMeta.textContent=`${profile.source} · ${formatDate(profile.measuredAt)} · ${profile.rig||'Rig not supplied'} · ${profile.target||'Flat target'}`;
  rawProfileLine.setAttribute('d',measurementPath(profile,'raw')); targetProfileLine.setAttribute('d',measurementPath(profile,'target')); correctionProfileLine.setAttribute('d',measurementPath(profile,'correction'));
  measurementRawPeak.textContent=dbLabel(rawPeak); measurementCorrectionPeak.textContent=dbLabel(correctionPeak); measurementPreamp.textContent=dbLabel(Math.max(-12,Math.min(11,-1-correctionPeak)));
  measurementAudit.textContent=`${profile.frequenciesHz.length} points · ${profile.responseType==='raw'?'correction = target minus raw response':'correction = imported gainDb'}. The source remains unchanged.`;
}
function exportSelectedAudit(){
  if(!selectedMeasurement) return;
  const profile=selectedMeasurement;
  const correctionDb=profile.frequenciesHz.map((_,index)=>profile.responseType==='raw'?(profile.targetDb[index]||0)-profile.gainDb[index]:profile.gainDb[index]);
  const audit={schema:'getEQd-calibration-audit/v1',generatedAt:new Date().toISOString(),measurement:{schema:profile.schema,model:profile.model,source:profile.source,measuredAt:profile.measuredAt,rig:profile.rig,target:profile.target,responseType:profile.responseType,notes:profile.notes,frequenciesHz:profile.frequenciesHz,rawDb:profile.gainDb,targetDb:profile.targetDb,correctionDb},quickBandFrequenciesHz:targetFrequencies,quickBandCorrectionDb:targetFrequencies.map(frequency=>Math.max(-12,Math.min(11,Math.round(correctionAt(profile,frequency)*2)/2)))};
  const link=document.createElement('a'); link.href=URL.createObjectURL(new Blob([JSON.stringify(audit,null,2)],{type:'application/json'})); link.download=`${profile.model.replace(/[^a-z0-9]+/gi,'-').replace(/^-|-$/g,'')||'geteqd-measurement'}-audit.json`; link.click(); URL.revokeObjectURL(link.href); measurementAudit.textContent='Calibration audit exported. Raw, target, correction and quick-band landing are included.';
}
function currentListeningSettings(){
  return {bands:bands.map(band=>band.value),preamp:+preamp.value,limiter:+limiter.value,outputMode:outputMode.value,headphoneTarget:headphoneTarget.value,headphoneSoftware:headphoneSoftware.value,crossfeed:+crossfeed.value,stageWidth:+stageWidth.value,bypassed:document.querySelector('#bypass').classList.contains('on')};
}
function setBypass(enabled){
  const button=document.querySelector('#bypass'); button.classList.toggle('on',enabled); button.setAttribute('aria-pressed',enabled); button.innerHTML=`<span class="toggle-dot"></span> ${enabled?'Processing bypassed':'Bypass'}`; document.querySelector('#console').classList.toggle('bypassed',enabled);
}
function applyListeningProfile(profile){
  profile.settings.bands.forEach((value,index)=>{if(bands[index]) bands[index].value=Number(value);});
  preamp.value=profile.settings.preamp; limiter.value=profile.settings.limiter; if([...outputMode.options].some(option=>option.value===profile.settings.outputMode)) outputMode.value=profile.settings.outputMode; headphoneTarget.value=profile.settings.headphoneTarget; headphoneSoftware.value=profile.settings.headphoneSoftware; crossfeed.value=profile.settings.crossfeed; stageWidth.value=profile.settings.stageWidth; setBypass(!!profile.settings.bypassed); renderBands(); sync(); syncHeadphones(); document.querySelectorAll('.preset').forEach(button=>button.classList.remove('active')); document.querySelector('#console').scrollIntoView({behavior:'smooth',block:'start'}); saveStatus.textContent=`Following ${profile.name}. Hot-mod it, then save again when it feels right.`;
}
function renderSavedProfiles(){
  const profiles=safeSavedProfiles();
  savedProfileList.innerHTML=profiles.length ? profiles.map((profile,index)=>`<article class="profile-card saved-profile-card"><div class="profile-card-top"><span class="version-chip">SAVED · LOCAL</span><span class="profile-count">${formatDate(profile.savedAt)}</span></div><h3>${escapeHtml(profile.name)}</h3><p>${escapeHtml(profile.settings.outputMode)} · ${profile.settings.bands.map(value=>`${value>0?'+':''}${Number(value).toFixed(1)} dB`).join(' / ')}</p><div class="saved-profile-actions"><button class="profile-apply" type="button" data-saved-index="${index}">Follow this profile <span>→</span></button><button class="profile-delete" type="button" data-delete-index="${index}" aria-label="Delete ${escapeHtml(profile.name)}">Delete</button></div></article>`).join('') : '<div class="profile-empty"><strong>Save a listening state to follow it later.</strong><p>Hot-mod the console, name the sound, and keep the complete setup on this device.</p></div>';
  savedProfileList.querySelectorAll('[data-saved-index]').forEach(button=>button.addEventListener('click',()=>applyListeningProfile(profiles[+button.dataset.savedIndex])));
  savedProfileList.querySelectorAll('[data-delete-index]').forEach(button=>button.addEventListener('click',()=>{const next=profiles.filter((_,index)=>index!==+button.dataset.deleteIndex); localStorage.setItem(savedProfileStorageKey,JSON.stringify(next)); renderSavedProfiles(); saveStatus.textContent='Saved profile removed from this device.';}));
}
function saveListeningProfile(){
  const name=profileName.value.trim(); if(!name){saveStatus.textContent='Give this sound a name before saving.'; profileName.focus(); return;}
  const profiles=safeSavedProfiles().filter(profile=>profile.name.toLowerCase()!==name.toLowerCase()); profiles.unshift({name,savedAt:new Date().toISOString(),settings:currentListeningSettings()}); localStorage.setItem(savedProfileStorageKey,JSON.stringify(profiles)); renderSavedProfiles(); profileName.value=''; saveStatus.textContent=`Saved ${name}. You can follow it from Your listening profiles.`;
}

function renderBands(){
  grid.innerHTML = bands.map((b,i)=>`<article class="band-card"><div class="band-top"><span class="band-index">0${i+1}</span><span class="band-index">${b.value > 0 ? '+' : ''}${b.value.toFixed(1)}</span></div><div class="band-name">${b.name}</div><div class="band-role">${b.role}</div><div class="band-value" id="bandValue${i}">${b.value > 0 ? '+' : ''}${b.value.toFixed(1)} dB</div><div class="band-hz">${b.hz}</div><input data-band="${i}" type="range" min="-12" max="11" step="0.5" value="${b.value}" aria-label="${b.name} gain"></article>`).join('');
  grid.querySelectorAll('input').forEach(input=>input.addEventListener('input',e=>{bands[+e.target.dataset.band].value=+e.target.value; sync(); markUnsaved();}));
}
function curvePoints(){
  const gains=bands.map(b=>b.value); const points=[]; const width=1000; const height=300;
  for(let x=0;x<=width;x+=10){const t=x/width; const pos=t*(gains.length-1); const i=Math.min(gains.length-2,Math.floor(pos)); const f=pos-i; const smooth=f*f*(3-2*f); const gain=gains[i]+(gains[i+1]-gains[i])*smooth+(+preamp.value); const y=height/2-(gain/12)*(height*.43); points.push(`${x.toFixed(1)},${Math.max(8,Math.min(height-8,y)).toFixed(1)}`)} return points;
}
function sync(){
  bands.forEach((b,i)=>{const el=document.querySelector(`#bandValue${i}`); if(el)el.textContent=`${b.value>0?'+':''}${b.value.toFixed(1)} dB`; const top=document.querySelectorAll('.band-top .band-index')[i*2+1]; if(top)top.textContent=`${b.value>0?'+':''}${b.value.toFixed(1)}`;});
  const pts=curvePoints(); curveLine.setAttribute('d',`M ${pts.join(' L ')}`); curveFill.setAttribute('d',`M 0 150 L ${pts.join(' L ')} L 1000 150 Z`);
  const avg=bands.reduce((a,b)=>a+b.value,0)/bands.length + +preamp.value; curveReadout.textContent=`${avg>0?'+':''}${avg.toFixed(1)} dB`; document.querySelector('#preampValue').textContent=`${+preamp.value>0?'+':''}${(+preamp.value).toFixed(1)} dB`; document.querySelector('#limiterValue').textContent=`${(+limiter.value).toFixed(1)} dB`;
}
function syncHeadphones(){
  const active = outputMode.value.startsWith('Headphones');
  headphoneDeck.hidden = !active;
  document.querySelector('#crossfeedValue').textContent = `${crossfeed.value}%`;
  document.querySelector('#stageWidthValue').textContent = `${stageWidth.value}%`;
  headphoneDeck.style.setProperty('--stage-width', `${stageWidth.value}%`);
  headphoneDeck.dataset.target = headphoneTarget.value;
  headphoneDeck.dataset.software = headphoneSoftware.value;
}
document.querySelectorAll('.preset').forEach(button=>button.addEventListener('click',()=>{document.querySelectorAll('.preset').forEach(b=>b.classList.remove('active'));button.classList.add('active');presets[button.dataset.preset].forEach((v,i)=>{bands[i].value=v;});renderBands();sync();markUnsaved();}));
preamp.addEventListener('input',()=>{sync();markUnsaved();}); limiter.addEventListener('input',()=>{sync();markUnsaved();});
outputMode.addEventListener('change',()=>{syncHeadphones();markUnsaved();}); headphoneTarget.addEventListener('change',()=>{syncHeadphones();markUnsaved();}); headphoneSoftware.addEventListener('change',()=>{syncHeadphones();markUnsaved();}); crossfeed.addEventListener('input',()=>{syncHeadphones();markUnsaved();}); stageWidth.addEventListener('input',()=>{syncHeadphones();markUnsaved();});
document.querySelector('#bypass').addEventListener('click',e=>{setBypass(!e.currentTarget.classList.contains('on'));markUnsaved();});
saveProfile.addEventListener('click',saveListeningProfile); profileName.addEventListener('keydown',event=>{if(event.key==='Enter') saveListeningProfile();});
document.querySelector('#importProfile').addEventListener('click',()=>profileFile.click());
profileFile.addEventListener('change',async event=>{
  const file=event.target.files[0]; if(!file) return;
  try { const profile=validateProfile(JSON.parse(await file.text())); const profiles=safeProfiles().filter(item=>item.model.toLowerCase()!==profile.model.toLowerCase()); profiles.unshift(profile); localStorage.setItem(profileStorageKey,JSON.stringify(profiles)); selectedMeasurement=profile; renderProfiles(); renderMeasurementInspector(); profileStatus.textContent=`Imported ${profile.model}. The source file stays on your device.`; }
  catch(error) { profileStatus.textContent=`Import not accepted: ${error.message}`; }
  event.target.value='';
});
document.querySelector('#downloadTemplate').addEventListener('click',()=>{
  const template={schema:'getEQd-profile/v1',model:'Your headphone or speaker model',source:'Measurement source',measuredAt:'2026-09-12',rig:'Measurement rig and fixture',target:'Target curve name or flat',responseType:'raw',notes:'Add how this measurement was made.',frequenciesHz:[20,32,64,125,250,500,1000,2000,4000,8000,12000,16000,20000],gainDb:[0,0,0,0,0,0,0,0,0,0,0,0,0],targetDb:[0,0,0,0,0,0,0,0,0,0,0,0,0]};
  const link=document.createElement('a'); link.href=URL.createObjectURL(new Blob([JSON.stringify(template,null,2)],{type:'application/json'})); link.download='getEQd-profile-template.json'; link.click(); URL.revokeObjectURL(link.href);
});
applyMeasurement.addEventListener('click',()=>{if(selectedMeasurement) applyProfile(selectedMeasurement);});
exportMeasurement.addEventListener('click',exportSelectedAudit);
renderBands(); sync(); syncHeadphones();
renderProfiles();
renderSavedProfiles();
renderMeasurementInspector();
