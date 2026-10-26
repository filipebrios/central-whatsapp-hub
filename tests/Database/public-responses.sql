begin;
do $$
declare u uuid:=(select user_id from public.agenda_members where active order by created_at limit 1); c uuid:=gen_random_uuid();
begin
  if u is null then raise exception 'Fixture exige membro'; end if;
  insert into public.agenda_companies(id,name) values(c,'MODUX RESPONSE FIXTURE');
  insert into public.agenda_members(company_id,user_id,role) values(c,u,'admin');
  perform set_config('modux_test.company',c::text,true);
  perform set_config('request.jwt.claims',jsonb_build_object('sub',u,'role','authenticated')::text,true);
end $$;
set local role authenticated;
do $$
declare c uuid:=current_setting('modux_test.company')::uuid; a uuid; token jsonb; i int;
  today date:=(now() at time zone 'America/Sao_Paulo')::date;
begin
  for i in 1..6 loop
    a=gen_random_uuid();
    insert into public.agenda_appointments(id,company_id,client,phone,date,time,status,service_completed_date)
    values(a,c,'MODUX RESPONSE FIXTURE','contato privado',today+1,'10:00',case when i>=5 then 'concluido' else 'agendado' end,
      case when i>=5 then today-7 else null end);
    token=public.agenda_issue_response_token(c,a,case when i>=5 then 'pos_venda' else 'confirmacao' end,1);
    if (token->>'token') !~ '^[0-9a-f]{64}$' then raise exception 'Token sem 256 bits'; end if;
    if exists(select 1 from public.agenda_response_tokens where token_hash=token->>'token') then raise exception 'Token cru armazenado'; end if;
    perform set_config('modux_test.token'||i,token->>'token',true);
    perform set_config('modux_test.appt'||i,a::text,true);
    if i=2 then perform public.agenda_prepare_manual_message(c,a,'lembrete_d1',1); end if;
    if i=3 then update public.agenda_appointments set time='11:00' where id=a; end if;
    if i=6 then update public.agenda_appointments set feedback_score=4 where id=a; end if;
  end loop;
  begin
    perform public.agenda_issue_response_token(c,a,'pos_venda',1);
    raise exception 'Revisão antiga aceita';
  exception when serialization_failure then null; end;
  begin
    perform public.agenda_respond(current_setting('modux_test.token1'),'confirm');
    raise exception 'Membro pôde consumir sem servidor';
  exception when insufficient_privilege then null; end;
  begin
    update public.agenda_response_tokens set used_at=now();
    raise exception 'Membro alterou consumo';
  exception when insufficient_privilege then null; end;
  begin
    insert into public.agenda_response_tokens(token_hash,company_id,appointment_id,kind,expires_at)
    values(repeat('a',64),c,a,'pos_venda',now()+interval '100 years');
    raise exception 'Validade forjada aceita';
  exception when insufficient_privilege then null; end;
  perform set_config('request.jwt.claims',jsonb_build_object('sub',gen_random_uuid(),'role','authenticated')::text,true);
  if exists(select 1 from public.agenda_response_tokens where company_id=c) then raise exception 'RLS vazou tokens'; end if;
  begin
    perform public.agenda_issue_response_token(c,a,'pos_venda',2);
    raise exception 'Não membro emitiu token';
  exception when insufficient_privilege then null; end;
end $$;
reset role;
select set_config('request.jwt.claims','{}',true) is not null as server_claims_cleared;
set local role service_role;
do $$
declare c uuid:=current_setting('modux_test.company')::uuid; r jsonb; rev int;
begin
  r=public.agenda_respond(current_setting('modux_test.token1'),'lookup');
  if r->>'status'<>'available' or r ? 'phone' or r ? 'client' or r ? 'address' then raise exception 'Lookup inválido/vazou contato'; end if;
  if exists(select 1 from public.agenda_response_tokens where company_id=c and used_at is not null) then raise exception 'Lookup consumiu token'; end if;
  if public.agenda_respond(current_setting('modux_test.token1'),null)->>'status'<>'unavailable' then raise exception 'Operação nula aceita'; end if;
  r=public.agenda_respond(current_setting('modux_test.token1'),'confirm');
  if r->>'status'<>'recorded' or not exists(select 1 from public.agenda_appointments where id=current_setting('modux_test.appt1')::uuid and status='confirmado') then raise exception 'Confirmação falhou'; end if;
  select revision into rev from public.agenda_appointments where id=current_setting('modux_test.appt1')::uuid;
  perform public.agenda_respond(current_setting('modux_test.token1'),'cancel');
  if not exists(select 1 from public.agenda_appointments where id=current_setting('modux_test.appt1')::uuid and revision=rev and status='confirmado') then raise exception 'Token reutilizado alterou atendimento'; end if;
  perform public.agenda_respond(current_setting('modux_test.token2'),'cancel');
  if not exists(select 1 from public.agenda_appointments where id=current_setting('modux_test.appt2')::uuid and status='reagendar') or
    not exists(select 1 from public.agenda_message_queue where appointment_id=current_setting('modux_test.appt2')::uuid and state='cancelada') then raise exception 'Pedido não interrompeu lembrete'; end if;
  if public.agenda_respond(current_setting('modux_test.token3'),'confirm')->>'status'<>'unavailable' then raise exception 'Horário antigo aceito'; end if;
  update public.agenda_response_tokens set expires_at=now()-interval '1 second' where appointment_id=current_setting('modux_test.appt4')::uuid;
  if public.agenda_respond(current_setting('modux_test.token4'),'confirm')->>'status'<>'unavailable' then raise exception 'Expirado aceito'; end if;
  if public.agenda_respond(current_setting('modux_test.token5'),'feedback',11)->>'status'<>'unavailable' then raise exception 'Nota inválida aceita'; end if;
  if public.agenda_respond(current_setting('modux_test.token5'),'confirm')->>'status'<>'unavailable' then raise exception 'Tipo cruzado aceito'; end if;
  perform public.agenda_respond(current_setting('modux_test.token5'),'feedback',9,'Retorno fictício');
  if not exists(select 1 from public.agenda_appointments where id=current_setting('modux_test.appt5')::uuid and feedback_score=9 and feedback_note='Retorno fictício' and phone='contato privado') then raise exception 'NPS falhou'; end if;
  if public.agenda_respond(current_setting('modux_test.token6'),'feedback',10)->>'status'<>'unavailable' then raise exception 'Sobrescreveu nota existente'; end if;
  if public.agenda_respond(repeat('b',64),'confirm')->>'status'<>'unavailable' then raise exception 'Token desconhecido aceito'; end if;
end $$;
reset role;
set local role anon;
do $$ begin
  begin perform public.agenda_respond(repeat('b',64),'lookup'); raise exception 'Anon acessou RPC'; exception when insufficient_privilege then null; end;
  begin perform 1 from public.agenda_response_tokens; raise exception 'Anon leu tabela'; exception when insufficient_privilege then null; end;
end $$;
reset role;
rollback;
select 'PASS: links de uso único, validade, horário, NPS, isolamento e rollback' as result,
  (select count(*) from public.agenda_response_tokens) as persisted_tokens,
  (select count(*) from public.agenda_companies where name='MODUX RESPONSE FIXTURE') as leftover_fixtures;
