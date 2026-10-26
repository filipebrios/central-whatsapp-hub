-- Executar via conexão administrativa. Todos os fixtures são desfeitos por ROLLBACK.
begin;
do $$
declare
  test_user uuid := (select user_id from public.agenda_members where active order by created_at limit 1);
  company_a uuid := gen_random_uuid();
  company_b uuid := gen_random_uuid();
  foreign_appointment uuid := gen_random_uuid();
begin
  if test_user is null then raise exception 'Fixture exige um membro existente'; end if;
  insert into public.agenda_companies(id,name) values(company_a,'MODUX FIXTURE A'),(company_b,'MODUX FIXTURE B');
  insert into public.agenda_members(company_id,user_id,role) values(company_a,test_user,'admin');
  insert into public.agenda_appointments(id,company_id,client) values(foreign_appointment,company_b,'MODUX FIXTURE estrangeiro');
  perform set_config('modux_test.company_a',company_a::text,true);
  perform set_config('modux_test.company_b',company_b::text,true);
  perform set_config('modux_test.foreign',foreign_appointment::text,true);
  perform set_config('request.jwt.claims',jsonb_build_object('sub',test_user,'role','authenticated')::text,true);
end $$;
set local role authenticated;
do $$
declare
  company_a uuid := current_setting('modux_test.company_a')::uuid;
  company_b uuid := current_setting('modux_test.company_b')::uuid;
  first_id uuid := gen_random_uuid();
  second_id uuid := gen_random_uuid();
  linked public.agenda_calendar_links;
begin
  insert into public.agenda_appointments(id,company_id,client,phone,service_ids) values
    (first_id,company_a,'MODUX FIXTURE 1','contato fictício',array['desinsetizacao']),
    (second_id,company_a,'MODUX FIXTURE 2','',array[]::text[]);
  linked := public.agenda_link_google_event(company_a,first_id,'fixture@group.calendar.google.com','event1','"v1"',1);
  if linked.appointment_id<>first_id or linked.event_etag<>'"v1"' then raise exception 'Vínculo inicial incorreto'; end if;
  -- Repetir mesmo pedido: uma única linha, sem alterar a revisão do atendimento.
  perform public.agenda_link_google_event(company_a,first_id,'fixture@group.calendar.google.com','event1','"v1"',1);
  if (select count(*) from public.agenda_calendar_links where company_id=company_a)<>1 then
    raise exception 'Repetição duplicou vínculo';
  end if;
  if (select revision from public.agenda_appointments where id=first_id)<>1 then
    raise exception 'Vínculo alterou indevidamente o atendimento';
  end if;
  begin
    perform public.agenda_link_google_event(company_a,second_id,'fixture@group.calendar.google.com','event1','"v1"',1);
    raise exception 'Mesmo evento foi atribuído a dois atendimentos';
  exception when unique_violation then null; end;
  begin
    perform public.agenda_link_google_event(company_a,first_id,'fixture@group.calendar.google.com','event2','"v1"',1);
    raise exception 'Mesmo atendimento recebeu dois eventos no calendário';
  exception when unique_violation then null; end;
  update public.agenda_appointments set notes='fixture revisão' where id=second_id;
  begin
    perform public.agenda_link_google_event(company_a,second_id,'fixture@group.calendar.google.com','event2','"v1"',1);
    raise exception 'Revisão antiga foi aceita';
  exception when serialization_failure then null; end;
  perform public.agenda_reconcile_google_event(company_a,'fixture@group.calendar.google.com','event1',
    '"v1"','"v2"',1,'2026-10-27','10:00',90,false);
  if not exists(select 1 from public.agenda_appointments where id=first_id and date='2026-10-27'
    and time='10:00' and duration=90 and phone='contato fictício' and service_ids=array['desinsetizacao'] and revision=2) then
    raise exception 'Reconciliação perdeu dados operacionais ou não atualizou horário';
  end if;
  perform public.agenda_reconcile_google_event(company_a,'fixture@group.calendar.google.com','event1',
    '"v1"','"v2"',1,'2026-10-27','10:00',90,false);
  if (select revision from public.agenda_appointments where id=first_id)<>2 then
    raise exception 'Retentativa de reconciliação incrementou revisão';
  end if;
  begin
    perform public.agenda_reconcile_google_event(company_a,'fixture@group.calendar.google.com','event1',
      '"v1"','"v3"',2,'2026-10-28','11:00',60,false);
    raise exception 'ETag antigo sobrescreveu reconciliação';
  exception when serialization_failure then null; end;
  begin
    perform public.agenda_reconcile_google_event(company_a,'fixture@group.calendar.google.com','event1',
      '"v2"','"v3"',1,'2026-10-28','11:00',60,false);
    raise exception 'Revisão antiga sobrescreveu reconciliação';
  exception when serialization_failure then null; end;
  perform public.agenda_reconcile_google_event(company_a,'fixture@group.calendar.google.com','event1',
    '"v2"','"v2"',2,null,null,null,true);
  if not exists(select 1 from public.agenda_appointments where id=first_id and status='cancelado'
    and phone='contato fictício' and revision=3) then raise exception 'Cancelamento perdeu cadastro ou status'; end if;
  perform public.agenda_reconcile_google_event(company_a,'fixture@group.calendar.google.com','event1',
    '"v2"','"v2"',2,null,null,null,true);
  if (select revision from public.agenda_appointments where id=first_id)<>3 then
    raise exception 'Retentativa de cancelamento duplicou revisão';
  end if;
  begin
    perform public.agenda_link_google_event(company_b,current_setting('modux_test.foreign')::uuid,
      'fixture@group.calendar.google.com','foreign','"v1"',1);
    raise exception 'Acesso a outra empresa foi permitido';
  exception when insufficient_privilege then null; end;
  begin
    perform public.agenda_link_google_event(company_a,second_id,'','event2','"v1"',2);
    raise exception 'ID de calendário vazio aceito';
  exception when invalid_parameter_value then null; end;
  begin
    update public.agenda_calendar_links set appointment_id=second_id where company_id=company_a;
    raise exception 'Identidade do vínculo permitiu reassociação direta';
  exception when insufficient_privilege then null; end;
  declare
    message_appointment uuid := gen_random_uuid();
    nps_appointment uuid := gen_random_uuid();
    moved_appointment uuid := gen_random_uuid();
    job public.agenda_message_queue;
    today date := (now() at time zone 'America/Sao_Paulo')::date;
  begin
    insert into public.agenda_appointments(id,company_id,client,date,time,status)
    values(message_appointment,company_a,'MODUX FIXTURE mensagem',today,'10:00','agendado');
    job := public.agenda_prepare_manual_message(company_a,message_appointment,'lembrete_d0',1);
    if job.state<>'preparada' or job.created_by<>(select auth.uid()) then
      raise exception 'Preparação anunciou envio ou perdeu autoria';
    end if;
    perform public.agenda_prepare_manual_message(company_a,message_appointment,'lembrete_d0',1);
    if (select count(*) from public.agenda_message_queue where appointment_id=message_appointment)<>1 then
      raise exception 'Preparação repetida duplicou mensagem';
    end if;
    update public.agenda_message_queue set state='enviada_manual' where id=job.id;
    if not exists(select 1 from public.agenda_message_queue where id=job.id
      and handled_by=(select auth.uid()) and handled_at is not null and state='enviada_manual') then
      raise exception 'Envio manual não registrou operador/horário';
    end if;
    job := public.agenda_prepare_manual_message(company_a,message_appointment,'lembrete_d0',1);
    if job.state<>'enviada_manual' then raise exception 'Repetição apagou envio registrado'; end if;
    begin
      update public.agenda_message_queue set appointment_id=first_id where id=job.id;
      raise exception 'Identidade da mensagem pôde ser alterada';
    exception when insufficient_privilege then null; end;
    begin
      perform public.agenda_prepare_manual_message(company_a,message_appointment,'pos_venda',1);
      raise exception 'Pós-venda liberado sem conclusão e sete dias';
    exception when invalid_parameter_value then null; end;
    perform public.agenda_prepare_manual_message(company_a,message_appointment,'confirmacao',1);
    update public.agenda_appointments set status='cancelado' where id=message_appointment;
    if not exists(select 1 from public.agenda_message_queue where appointment_id=message_appointment
      and template_key='confirmacao' and state='cancelada') or
      not exists(select 1 from public.agenda_message_queue where appointment_id=message_appointment
      and template_key='lembrete_d0' and state='enviada_manual') then
      raise exception 'Cancelamento não distinguiu rascunho de mensagem enviada';
    end if;
    insert into public.agenda_appointments(id,company_id,client,date,time,status)
    values(moved_appointment,company_a,'MODUX FIXTURE remarcado',today,'10:00','agendado');
    job := public.agenda_prepare_manual_message(company_a,moved_appointment,'lembrete_d0',1);
    update public.agenda_appointments set time='11:00' where id=moved_appointment;
    if not exists(select 1 from public.agenda_message_queue where id=job.id and state='cancelada') then
      raise exception 'Rascunho com horário antigo permaneceu ativo';
    end if;
    insert into public.agenda_appointments(id,company_id,client,status,service_completed_date,feedback_score)
    values(nps_appointment,company_a,'MODUX FIXTURE NPS','concluido',today-7,8);
    job := public.agenda_prepare_manual_message(company_a,nps_appointment,'pos_venda',1);
    if job.occurrence_key<>(today-7)::text then raise exception 'Ocorrência NPS não usa conclusão real'; end if;
    job := public.agenda_prepare_manual_message(company_a,nps_appointment,'avaliacao_google',1);
    if job.state<>'preparada' then raise exception 'Convite não ficou pendente de revisão humana'; end if;
    update public.agenda_appointments set feedback_score=7 where id=nps_appointment;
    if not exists(select 1 from public.agenda_message_queue where appointment_id=nps_appointment
      and template_key='pos_venda' and state='tratada_manual') or
      not exists(select 1 from public.agenda_message_queue where appointment_id=nps_appointment
      and template_key='avaliacao_google' and state='cancelada') then
      raise exception 'Retorno do cliente não encerrou pendências corretamente';
    end if;
  end;
  perform set_config('request.jwt.claims',jsonb_build_object('sub',gen_random_uuid(),'role','authenticated')::text,true);
  if exists(select 1 from public.agenda_calendar_links where company_id=company_a) then
    raise exception 'RLS expôs vínculos a usuário não membro';
  end if;
end $$;
set local role anon;
do $$
begin
  begin
    perform public.agenda_link_google_event(gen_random_uuid(),gen_random_uuid(),'x','y','z',1);
    raise exception 'RPC acessível anonimamente';
  exception when insufficient_privilege then null; end;
  begin
    perform 1 from public.agenda_calendar_links limit 1;
    raise exception 'Tabela acessível anonimamente';
  exception when insufficient_privilege then null; end;
end $$;
rollback;
select 'PASS: vínculos/reconciliação/fila manual/NPS, idempotência, conflitos, RLS e rollback' as result,
       (select count(*) from public.agenda_calendar_links) as persisted_links,
       (select count(*) from public.agenda_companies where name like 'MODUX FIXTURE%') as leftover_fixtures;
