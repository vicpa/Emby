define(["apphost","globalize","connectionManager","layoutManager","focusManager","scrollHelper","appSettings","registrationServices","dialogHelper","paper-icon-button-light","formDialogStyle"],function(appHost,globalize,connectionManager,layoutManager,focusManager,scrollHelper,appSettings,registrationServices,dialogHelper){"use strict";function submitJob(dlg,apiClient,userId,syncOptions,form){if(!userId)throw new Error("userId cannot be null");if(!syncOptions)throw new Error("syncOptions cannot be null");if(!form)throw new Error("form cannot be null");var selectSyncTarget=form.querySelector("#selectSyncTarget"),target=selectSyncTarget?selectSyncTarget.value:null;if(!target)return require(["toast"],function(toast){toast(globalize.translate("sharedcomponents#PleaseSelectDeviceToSyncTo"))}),!1;var options={userId:userId,TargetId:target,ParentId:syncOptions.ParentId,Category:syncOptions.Category};return setJobValues(options,form),syncOptions.items&&syncOptions.items.length&&(options.ItemIds=(syncOptions.items||[]).map(function(i){return i.Id||i}).join(",")),apiClient.ajax({type:"POST",url:apiClient.getUrl("Sync/Jobs"),data:JSON.stringify(options),contentType:"application/json",dataType:"json"}).then(function(){dialogHelper.close(dlg),require(["toast"],function(toast){showSubmissionToast(target,apiClient),"download"===syncOptions.mode&&syncNow()})}),!0}function showSubmissionToast(targetId,apiClient){require(["toast"],function(toast){toast(targetId===apiClient.deviceId()?globalize.translate("sharedcomponents#DownloadingDots"):globalize.translate("sharedcomponents#SyncingDots"))})}function syncNow(){require(["localsync"],function(localSync){localSync.sync()})}function submitQuickSyncJob(apiClient,userId,targetId,syncOptions){if(!userId)throw new Error("userId cannot be null");if(!syncOptions)throw new Error("syncOptions cannot be null");if(!targetId)throw new Error("targetId cannot be null");var options={userId:userId,TargetId:targetId,ParentId:syncOptions.ParentId,Category:syncOptions.Category,Quality:syncOptions.Quality,Bitrate:syncOptions.Bitrate};return syncOptions.items&&syncOptions.items.length&&(options.ItemIds=(syncOptions.items||[]).map(function(i){return i.Id||i}).join(",")),apiClient.ajax({type:"POST",url:apiClient.getUrl("Sync/Jobs"),data:JSON.stringify(options),contentType:"application/json",dataType:"json"}).then(function(){require(["toast"],function(toast){showSubmissionToast(targetId,apiClient),"download"===syncOptions.mode&&syncNow()})})}function setJobValues(job,form){var txtBitrate=form.querySelector("#txtBitrate"),bitrate=txtBitrate?txtBitrate.value:null;bitrate&&(bitrate=1e6*parseFloat(bitrate)),job.Bitrate=bitrate;var selectQuality=form.querySelector("#selectQuality");selectQuality&&(job.Quality=selectQuality.value,appSettings.set("sync-lastquality",job.Quality||""));var selectProfile=form.querySelector("#selectProfile");selectProfile&&(job.Profile=selectProfile.value);var txtItemLimit=form.querySelector("#txtItemLimit");txtItemLimit&&(job.ItemLimit=txtItemLimit.value||null);var chkSyncNewContent=form.querySelector("#chkSyncNewContent");chkSyncNewContent&&(job.SyncNewContent=chkSyncNewContent.checked);var chkUnwatchedOnly=form.querySelector("#chkUnwatchedOnly");chkUnwatchedOnly&&(job.UnwatchedOnly=chkUnwatchedOnly.checked)}function renderForm(options){return new Promise(function(resolve,reject){require(["emby-checkbox","emby-input","emby-select"],function(){renderFormInternal(options,connectionManager.deviceId(),resolve)})})}function renderFormInternal(options,defaultTargetId,resolve){var elem=options.elem,dialogOptions=options.dialogOptions,targets=dialogOptions.Targets,html="",mode=options.mode,targetContainerClass="download"===mode?" hide":"",syncTargetLabel="convert"===mode?globalize.translate("sharedcomponents#LabelConvertTo"):globalize.translate("sharedcomponents#LabelSyncTo");options.readOnlySyncTarget?(html+='<div class="inputContainer'+targetContainerClass+'">',html+='<input is="emby-input" type="text" id="selectSyncTarget" readonly label="'+syncTargetLabel+'"/>',html+="</div>"):(html+='<div class="selectContainer'+targetContainerClass+'">',html+='<select is="emby-select" id="selectSyncTarget" required="required" label="'+syncTargetLabel+'">',html+=targets.map(function(t){return"<option"+(defaultTargetId===t.Id?' selected="selected"':"")+' value="'+t.Id+'">'+t.Name+"</option>"}).join(""),html+="</select>",targets.length||(html+='<div class="fieldDescription">'+globalize.translate("sharedcomponents#LabelSyncNoTargetsHelp")+"</div>"),appHost.supports("externallinks")&&(html+='<div class="fieldDescription"><a is="emby-linkbutton" class="button-link lnkLearnMore" href="https://github.com/MediaBrowser/Wiki/wiki/Sync" target="_blank">'+globalize.translate("sharedcomponents#LearnMore")+"</a></div>"),html+="</div>"),html+='<div class="fldProfile selectContainer hide">',html+='<select is="emby-select" id="selectProfile" label="'+globalize.translate("sharedcomponents#LabelProfile")+'">',html+="</select>",html+='<div class="fieldDescription profileDescription"></div>',html+="</div>",html+='<div class="fldQuality selectContainer hide">',html+='<select is="emby-select" id="selectQuality" required="required" label="'+globalize.translate("sharedcomponents#LabelQuality")+'">',html+="</select>",html+='<div class="fieldDescription qualityDescription"></div>',html+="</div>",html+='<div class="fldBitrate inputContainer hide">',html+='<input is="emby-input" type="number" step=".1" min=".1" id="txtBitrate" label="'+globalize.translate("sharedcomponents#LabelBitrateMbps")+'"/>',html+="</div>",-1!==dialogOptions.Options.indexOf("UnwatchedOnly")&&(html+='<div class="checkboxContainer checkboxContainer-withDescription">',html+="<label>",html+='<input is="emby-checkbox" type="checkbox" id="chkUnwatchedOnly"/>',html+="convert"===mode?"<span>"+globalize.translate("sharedcomponents#ConvertUnwatchedVideosOnly")+"</span>":"<span>"+globalize.translate("sharedcomponents#SyncUnwatchedVideosOnly")+"</span>",html+="</label>",html+="convert"===mode?'<div class="fieldDescription checkboxFieldDescription">'+globalize.translate("sharedcomponents#ConvertUnwatchedVideosOnlyHelp")+"</div>":'<div class="fieldDescription checkboxFieldDescription">'+globalize.translate("sharedcomponents#SyncUnwatchedVideosOnlyHelp")+"</div>",html+="</div>"),-1!==dialogOptions.Options.indexOf("SyncNewContent")&&(html+='<div class="checkboxContainer checkboxContainer-withDescription">',html+="<label>",html+='<input is="emby-checkbox" type="checkbox" id="chkSyncNewContent"/>',html+="convert"===mode?"<span>"+globalize.translate("sharedcomponents#AutomaticallyConvertNewContent")+"</span>":"<span>"+globalize.translate("sharedcomponents#AutomaticallySyncNewContent")+"</span>",html+="</label>",html+="convert"===mode?'<div class="fieldDescription checkboxFieldDescription">'+globalize.translate("sharedcomponents#AutomaticallyConvertNewContentHelp")+"</div>":'<div class="fieldDescription checkboxFieldDescription">'+globalize.translate("sharedcomponents#AutomaticallySyncNewContentHelp")+"</div>",html+="</div>"),-1!==dialogOptions.Options.indexOf("ItemLimit")&&(html+='<div class="inputContainer">',html+='<input is="emby-input" type="number" step="1" min="1" id="txtItemLimit" label="'+globalize.translate("sharedcomponents#LabelItemLimit")+'"/>',html+="convert"===mode?'<div class="fieldDescription">'+globalize.translate("sharedcomponents#ConvertItemLimitHelp")+"</div>":'<div class="fieldDescription">'+globalize.translate("sharedcomponents#DownloadItemLimitHelp")+"</div>",html+="</div>"),elem.innerHTML=html;var selectSyncTarget=elem.querySelector("#selectSyncTarget");selectSyncTarget&&(selectSyncTarget.addEventListener("change",function(){loadQualityOptions(elem,this.value,options.dialogOptionsFn).then(resolve)}),selectSyncTarget.dispatchEvent(new CustomEvent("change",{bubbles:!0})));var selectProfile=elem.querySelector("#selectProfile");selectProfile&&(selectProfile.addEventListener("change",function(){onProfileChange(elem,this.value)}),dialogOptions.ProfileOptions.length&&selectProfile.dispatchEvent(new CustomEvent("change",{bubbles:!0})));var selectQuality=elem.querySelector("#selectQuality");selectQuality&&(selectQuality.addEventListener("change",function(){onQualityChange(elem,this.value)}),selectQuality.dispatchEvent(new CustomEvent("change",{bubbles:!0}))),setTimeout(function(){focusManager.autoFocus(elem)},100)}function showWifiMessage(){require(["dialog","appRouter"],function(dialog,appRouter){var options={title:globalize.translate("sharedcomponents#HeaderWaitingForWifi"),text:globalize.translate("sharedcomponents#WifiRequiredToDownload")},items=[];items.push({name:options.confirmText||globalize.translate("sharedcomponents#ButtonOk"),id:"ok",type:"submit"}),items.push({name:options.cancelText||globalize.translate("sharedcomponents#HeaderDownloadSettings"),id:"downloadsettings",type:"cancel"}),options.buttons=items,dialog(options).then(function(result){return"ok"===result?Promise.resolve():"downloadsettings"===result?(appRouter.show(appRouter.getRouteUrl("downloadsettings")),Promise.resolve()):Promise.reject()})})}function validateNetwork(){switch(navigator.connection?navigator.connection.type:null){case"cellular":case"bluetooth":return showWifiMessage(),!1;default:return!0}}function showSyncMenu(options){return"download"===options.mode&&appSettings.syncOnlyOnWifi()&&!validateNetwork()?Promise.reject():registrationServices.validateFeature("sync").then(function(){return showSyncMenuInternal(options)})}function enableAutoSync(options){if("download"!==options.mode)return!1;var firstItem=(options.items||[])[0]||{};return"Audio"===firstItem.Type||("MusicAlbum"===firstItem.Type||("MusicArtist"===firstItem.Type||("MusicGenre"===firstItem.Type||"Playlist"===firstItem.Type&&"Audio"===firstItem.MediaType)))}function showSyncMenuInternal(options){var apiClient=connectionManager.getApiClient(options.serverId),userId=apiClient.getCurrentUserId();if(enableAutoSync(options))return submitQuickSyncJob(apiClient,userId,apiClient.deviceId(),{items:options.items,Quality:"custom",Bitrate:appSettings.maxStaticMusicBitrate()});var dialogOptionsFn=getTargetDialogOptionsFn(apiClient,{UserId:userId,ItemIds:(options.items||[]).map(function(i){return i.Id||i}).join(","),ParentId:options.ParentId,Category:options.Category,IncludeProviders:"convert"===options.mode?"ConvertSyncProvider":null,ExcludeProviders:"convert"===options.mode?null:"ConvertSyncProvider"});return dialogOptionsFn().then(function(dialogOptions){currentDialogOptions=dialogOptions;var dlgElementOptions={removeOnClose:!0,scrollY:!1,autoFocus:!1};layoutManager.tv?dlgElementOptions.size="fullscreen":dlgElementOptions.size="small";var dlg=dialogHelper.createDialog(dlgElementOptions);dlg.classList.add("formDialog");var html="";html+='<div class="formDialogHeader">',html+='<button is="paper-icon-button-light" class="btnCancel autoSize" tabindex="-1"><i class="md-icon">&#xE5C4;</i></button>',html+='<h3 class="formDialogHeaderTitle">';var syncButtonLabel="download"===options.mode?globalize.translate("sharedcomponents#Download"):"convert"===options.mode?globalize.translate("sharedcomponents#Convert"):globalize.translate("sharedcomponents#Sync");html+=syncButtonLabel,html+="</h3>",appHost.supports("externallinks")&&(html+='<a is="emby-linkbutton" href="https://github.com/MediaBrowser/Wiki/wiki/Sync" target="_blank" class="button-link lnkHelp" style="margin-top:0;display:inline-block;vertical-align:middle;margin-left:auto;"><i class="md-icon">info</i><span>'+globalize.translate("sharedcomponents#Help")+"</span></a>"),html+="</div>",html+='<div class="formDialogContent smoothScrollY" style="padding-top:2em;">',html+='<div class="dialogContentInner dialog-content-centered">',html+='<form class="formSubmitSyncRequest" style="margin: auto;">',html+='<div class="formFields"></div>',html+='<div class="formDialogFooter">',html+='<button is="emby-button" type="submit" class="raised button-submit block formDialogFooterItem"><span>'+syncButtonLabel+"</span></button>",html+="</div>",html+="</form>",html+="</div>",html+="</div>",dlg.innerHTML=html;var submitted=!1;dlg.querySelector("form").addEventListener("submit",function(e){return submitted=submitJob(dlg,apiClient,userId,options,this),e.preventDefault(),!1}),dlg.querySelector(".btnCancel").addEventListener("click",function(){dialogHelper.close(dlg)}),layoutManager.tv&&scrollHelper.centerFocus.on(dlg.querySelector(".formDialogContent"),!1);var promise=dialogHelper.open(dlg);return renderForm({elem:dlg.querySelector(".formFields"),dialogOptions:dialogOptions,dialogOptionsFn:dialogOptionsFn,mode:options.mode}),promise.then(function(){return layoutManager.tv&&scrollHelper.centerFocus.off(dlg.querySelector(".formDialogContent"),!1),submitted?Promise.resolve():Promise.reject()})})}function getTargetDialogOptionsFn(apiClient,query){return function(targetId){return query.TargetId=targetId,apiClient.getJSON(apiClient.getUrl("Sync/Options",query))}}function setQualityFieldVisible(form,visible){var fldQuality=form.querySelector(".fldQuality"),selectQuality=form.querySelector("#selectQuality");visible?(fldQuality&&fldQuality.classList.remove("hide"),selectQuality&&selectQuality.removeAttribute("required")):(fldQuality&&fldQuality.classList.add("hide"),selectQuality&&selectQuality.removeAttribute("required"))}function onProfileChange(form,profileId){var options=currentDialogOptions||{},profileOptions=options.ProfileOptions||[];if(profileOptions.length){var option=profileOptions.filter(function(o){return o.Id===profileId})[0],qualityOptions=options.QualityOptions||[];option?(form.querySelector(".profileDescription").innerHTML=option.Description||"",setQualityFieldVisible(form,qualityOptions.length>0&&option.EnableQualityOptions&&-1!==options.Options.indexOf("Quality"))):(form.querySelector(".profileDescription").innerHTML="",setQualityFieldVisible(form,qualityOptions.length>0&&-1!==options.Options.indexOf("Quality")))}}function onQualityChange(form,qualityId){var options=currentDialogOptions||{},option=(options.QualityOptions||[]).filter(function(o){return o.Id===qualityId})[0],qualityDescription=form.querySelector(".qualityDescription");qualityDescription.innerHTML=option?option.Description||"":"";var fldBitrate=form.querySelector(".fldBitrate"),txtBitrate=form.querySelector("#txtBitrate");"custom"===qualityId?(fldBitrate&&fldBitrate.classList.remove("hide"),txtBitrate&&txtBitrate.setAttribute("required","required")):(fldBitrate&&fldBitrate.classList.add("hide"),txtBitrate&&txtBitrate.removeAttribute("required"))}function renderTargetDialogOptions(form,options){currentDialogOptions=options;var fldProfile=form.querySelector(".fldProfile"),selectProfile=form.querySelector("#selectProfile");options.ProfileOptions.length&&-1!==options.Options.indexOf("Profile")?(fldProfile&&fldProfile.classList.remove("hide"),selectProfile&&selectProfile.setAttribute("required","required")):(fldProfile&&fldProfile.classList.add("hide"),selectProfile&&selectProfile.removeAttribute("required")),setQualityFieldVisible(form,options.QualityOptions.length>0),selectProfile&&(selectProfile.innerHTML=options.ProfileOptions.map(function(o){var selectedAttribute=o.IsDefault?' selected="selected"':"";return'<option value="'+o.Id+'"'+selectedAttribute+">"+o.Name+"</option>"}).join(""),selectProfile.dispatchEvent(new CustomEvent("change",{bubbles:!0})));var selectQuality=form.querySelector("#selectQuality");if(selectQuality){selectQuality.innerHTML=options.QualityOptions.map(function(o){var selectedAttribute=o.IsDefault?' selected="selected"':"";return'<option value="'+o.Id+'"'+selectedAttribute+">"+o.Name+"</option>"}).join("");var lastQuality=appSettings.get("sync-lastquality");lastQuality&&options.QualityOptions.filter(function(i){return i.Id===lastQuality}).length&&(selectQuality.value=lastQuality),selectQuality.dispatchEvent(new CustomEvent("change",{bubbles:!0}))}}function loadQualityOptions(form,targetId,dialogOptionsFn){return dialogOptionsFn(targetId).then(function(options){return renderTargetDialogOptions(form,options)})}var currentDialogOptions;return{showMenu:showSyncMenu,renderForm:renderForm,setJobValues:setJobValues}});